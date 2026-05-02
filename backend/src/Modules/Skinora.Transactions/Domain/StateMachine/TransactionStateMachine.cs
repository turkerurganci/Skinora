using Skinora.Shared.Enums;
using Skinora.Shared.Exceptions;
using Skinora.Transactions.Domain.Entities;
using Stateless;

namespace Skinora.Transactions.Domain.StateMachine;

/// <summary>
/// Declarative state machine for <see cref="Transaction"/> per 05 §4.1–§4.5
/// and 09 §9.2. Wraps the Stateless library and surfaces invalid transitions
/// as <see cref="DomainException"/>.
/// </summary>
/// <remarks>
/// State machine boundary (09 §9.2): only domain primitives are mutated here
/// (milestone timestamps, cancellation fields, emergency hold flags). Hangfire
/// scheduling, notifications and HTTP calls are application-layer side effects
/// performed by callers after a successful Fire(); they are forward-deferred
/// to T47 (timeouts) and T62 (notifications).
/// </remarks>
public class TransactionStateMachine
{
    public const string InvalidTransitionErrorCode = "TRANSACTION_INVALID_STATE_TRANSITION";
    public const string OnHoldErrorCode = "TRANSACTION_ON_HOLD";
    public const string RowVersionMismatchErrorCode = "TRANSACTION_ROWVERSION_MISMATCH";
    public const string MissingRequiredFieldErrorCode = "TRANSACTION_MISSING_REQUIRED_FIELD";
    public const string CancelReasonRequiredErrorCode = "TRANSACTION_CANCEL_REASON_REQUIRED";
    public const string AlreadyOnHoldErrorCode = "TRANSACTION_ALREADY_ON_HOLD";
    public const string NotOnHoldErrorCode = "TRANSACTION_NOT_ON_HOLD";
    public const string EmergencyHoldReasonRequiredErrorCode = "TRANSACTION_EMERGENCY_HOLD_REASON_REQUIRED";

    private readonly Transaction _transaction;
    private readonly byte[]? _expectedRowVersion;
    private readonly StateMachine<TransactionStatus, TransactionTrigger> _machine;

    public TransactionStateMachine(Transaction transaction, byte[]? expectedRowVersion = null)
    {
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _expectedRowVersion = expectedRowVersion;
        _machine = new StateMachine<TransactionStatus, TransactionTrigger>(
            () => _transaction.Status,
            s => _transaction.Status = s);
        ConfigureTransitions();
    }

    public TransactionStatus State => _machine.State;

    public IEnumerable<TransactionTrigger> PermittedTriggers
    {
        get
        {
            // Stateless 5.x marks the sync overload obsolete in favor of the async variant.
            // The state machine here is fully synchronous (no async OnEntry/guards), so the
            // sync API is the correct fit; suppress the obsolete warning for this wrapper.
#pragma warning disable CS0618
            return _machine.GetPermittedTriggers();
#pragma warning restore CS0618
        }
    }

    public bool CanFire(TransactionTrigger trigger) => _machine.CanFire(trigger);

    /// <summary>Fires a non-cancellation trigger (forward path, Timeout, AdminApprove, AdminReject).</summary>
    public void Fire(TransactionTrigger trigger) => FireInternal(trigger, ctx: null);

    /// <summary>Fires a caller-initiated cancellation trigger with a reason.</summary>
    public void Fire(TransactionTrigger trigger, CancellationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        FireInternal(trigger, context);
    }

    public void ApplyEmergencyHold(Guid adminId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(EmergencyHoldReasonRequiredErrorCode, "Emergency hold sebebi zorunlu (05 §4.5).");
        }
        if (_transaction.IsOnHold)
        {
            throw new DomainException(AlreadyOnHoldErrorCode, "İşlem zaten emergency hold altında.");
        }
        EnforceRowVersion();

        var now = DateTime.UtcNow;
        _transaction.IsOnHold = true;
        _transaction.EmergencyHoldAt = now;
        _transaction.EmergencyHoldReason = reason;
        _transaction.EmergencyHoldByAdminId = adminId;
        _transaction.PreviousStatusBeforeHold = (int)_transaction.Status;
        _transaction.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        _transaction.TimeoutFrozenAt = now;

        if (_transaction.Status == TransactionStatus.ITEM_ESCROWED && _transaction.PaymentDeadline.HasValue)
        {
            var remaining = (_transaction.PaymentDeadline.Value - now).TotalSeconds;
            _transaction.TimeoutRemainingSeconds = remaining > 0 ? (int)Math.Floor(remaining) : 0;
        }
    }

    public void ReleaseEmergencyHold()
    {
        if (!_transaction.IsOnHold)
        {
            throw new DomainException(NotOnHoldErrorCode, "İşlem emergency hold altında değil.");
        }
        EnforceRowVersion();

        _transaction.IsOnHold = false;
        _transaction.TimeoutFreezeReason = null;
        _transaction.TimeoutFrozenAt = null;
        // PreviousStatusBeforeHold + EmergencyHold* timestamps stay for audit (05 §4.5).
        // TimeoutRemainingSeconds preserved for T47 reschedule.
    }

    private void FireInternal(TransactionTrigger trigger, CancellationContext? ctx)
    {
        EnforceRowVersion();
        EnforceNotOnHold(trigger);

        if (!_machine.CanFire(trigger))
        {
            throw new DomainException(
                InvalidTransitionErrorCode,
                $"Geçersiz geçiş: {_machine.State} -> {trigger} (05 §4.2).");
        }

        ApplyCancellationFields(trigger, ctx);
        _machine.Fire(trigger);
    }

    private void EnforceRowVersion()
    {
        if (_expectedRowVersion is null)
        {
            return;
        }
        if (!_expectedRowVersion.AsSpan().SequenceEqual(_transaction.RowVersion))
        {
            throw new DomainException(
                RowVersionMismatchErrorCode,
                "Transaction RowVersion uyumsuz — eski snapshot ile state geçişi reddedildi.");
        }
    }

    private void EnforceNotOnHold(TransactionTrigger trigger)
    {
        if (_transaction.IsOnHold)
        {
            throw new DomainException(
                OnHoldErrorCode,
                $"İşlem emergency hold altında — '{trigger}' tetikleyicisi reddedildi (05 §4.5).");
        }
    }

    private void ApplyCancellationFields(TransactionTrigger trigger, CancellationContext? ctx)
    {
        var cancelledBy = trigger switch
        {
            TransactionTrigger.Timeout => CancelledByType.TIMEOUT,
            TransactionTrigger.SellerCancel or TransactionTrigger.SellerDecline => CancelledByType.SELLER,
            TransactionTrigger.BuyerCancel or TransactionTrigger.BuyerDecline => CancelledByType.BUYER,
            TransactionTrigger.AdminCancel or TransactionTrigger.AdminReject => CancelledByType.ADMIN,
            _ => (CancelledByType?)null,
        };

        if (cancelledBy is null)
        {
            return;
        }

        var defaultReason = trigger switch
        {
            TransactionTrigger.Timeout => "Timeout: işlem süresi içinde tamamlanmadı",
            TransactionTrigger.AdminReject => "Flag reddedildi (admin)",
            _ => null,
        };

        var reason = ctx?.CancelReason ?? defaultReason;
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                CancelReasonRequiredErrorCode,
                $"İptal sebebi zorunlu ('{trigger}').");
        }

        _transaction.CancelledBy = cancelledBy;
        _transaction.CancelReason = reason;
    }

    private void ConfigureTransitions()
    {
        // CREATED — alıcı bekleniyor
        _machine.Configure(TransactionStatus.CREATED)
            .PermitIf(TransactionTrigger.BuyerAccept, TransactionStatus.ACCEPTED, HasFieldsForAccepted, "BuyerId ve BuyerRefundAddress zorunlu (06 §3.5).")
            .Permit(TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT)
            .Permit(TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER)
            .Permit(TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER)
            .Permit(TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN);

        // ACCEPTED — satıcıdan trade offer bekleniyor
        _machine.Configure(TransactionStatus.ACCEPTED)
            .OnEntry(() => _transaction.AcceptedAt = DateTime.UtcNow)
            .Permit(TransactionTrigger.SendTradeOfferToSeller, TransactionStatus.TRADE_OFFER_SENT_TO_SELLER)
            .Permit(TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT)
            .Permit(TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER)
            .Permit(TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER)
            .Permit(TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN);

        // TRADE_OFFER_SENT_TO_SELLER
        _machine.Configure(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER)
            .PermitIf(TransactionTrigger.EscrowItem, TransactionStatus.ITEM_ESCROWED, HasFieldsForItemEscrowed, "EscrowBotAssetId zorunlu (06 §3.5).")
            .Permit(TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT)
            .Permit(TransactionTrigger.SellerDecline, TransactionStatus.CANCELLED_SELLER)
            .Permit(TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER)
            .Permit(TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN);

        // ITEM_ESCROWED — ödeme bekleniyor (timeout warning aktif)
        _machine.Configure(TransactionStatus.ITEM_ESCROWED)
            .OnEntry(() =>
            {
                _transaction.ItemEscrowedAt = DateTime.UtcNow;
                _transaction.TimeoutWarningSentAt = null;
            })
            .OnExit(() =>
            {
                _transaction.TimeoutWarningJobId = null;
                _transaction.TimeoutWarningSentAt = null;
            })
            .Permit(TransactionTrigger.ConfirmPayment, TransactionStatus.PAYMENT_RECEIVED)
            .Permit(TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT)
            .Permit(TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER)
            // ITEM_ESCROWED tanımı gereği ödeme henüz yok; guard 09 §9.2 örneğindeki açık kuralla
            // korunur (PAYMENT_RECEIVED state'ine geçildiğinde alıcı iptali otomatik yasak olur).
            .PermitIf(TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER, () => _transaction.PaymentReceivedAt is null, "Ödeme yapıldıktan sonra alıcı iptal edemez (02 §7).")
            .Permit(TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN);

        // PAYMENT_RECEIVED — alıcıya teslim aşaması; tek taraflı iptal kapalı (02 §7)
        _machine.Configure(TransactionStatus.PAYMENT_RECEIVED)
            .OnEntry(() => _transaction.PaymentReceivedAt = DateTime.UtcNow)
            .Permit(TransactionTrigger.SendTradeOfferToBuyer, TransactionStatus.TRADE_OFFER_SENT_TO_BUYER)
            .Permit(TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN);

        // TRADE_OFFER_SENT_TO_BUYER
        _machine.Configure(TransactionStatus.TRADE_OFFER_SENT_TO_BUYER)
            .PermitIf(TransactionTrigger.DeliverItem, TransactionStatus.ITEM_DELIVERED, HasFieldsForItemDelivered, "DeliveredBuyerAssetId zorunlu (06 §3.5).")
            .Permit(TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT)
            .Permit(TransactionTrigger.BuyerDecline, TransactionStatus.CANCELLED_BUYER)
            .Permit(TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN);

        // ITEM_DELIVERED — admin cancel kullanılamaz (05 §4.2 not)
        _machine.Configure(TransactionStatus.ITEM_DELIVERED)
            .OnEntry(() => _transaction.ItemDeliveredAt = DateTime.UtcNow)
            .Permit(TransactionTrigger.Complete, TransactionStatus.COMPLETED);

        // COMPLETED — terminal
        _machine.Configure(TransactionStatus.COMPLETED)
            .OnEntry(() => _transaction.CompletedAt = DateTime.UtcNow);

        // CANCELLED_* — terminal; OnEntry CancelledAt set'ler. CancelledBy/CancelReason FireInternal'de set edilir.
        _machine.Configure(TransactionStatus.CANCELLED_TIMEOUT)
            .OnEntry(() => _transaction.CancelledAt = DateTime.UtcNow);
        _machine.Configure(TransactionStatus.CANCELLED_SELLER)
            .OnEntry(() => _transaction.CancelledAt = DateTime.UtcNow);
        _machine.Configure(TransactionStatus.CANCELLED_BUYER)
            .OnEntry(() => _transaction.CancelledAt = DateTime.UtcNow);
        _machine.Configure(TransactionStatus.CANCELLED_ADMIN)
            .OnEntry(() => _transaction.CancelledAt = DateTime.UtcNow);

        // FLAGGED — yalnızca işlem oluşturma anında set edilir (05 §4.2 not).
        // Admin onayında CREATED'a, reddinde CANCELLED_ADMIN'e geçer (03 §7.1).
        _machine.Configure(TransactionStatus.FLAGGED)
            .PermitIf(TransactionTrigger.AdminApprove, TransactionStatus.CREATED, HasFlaggedStateInvariant, "FLAGGED state invariant ihlali (06 §3.5).")
            .PermitIf(TransactionTrigger.AdminReject, TransactionStatus.CANCELLED_ADMIN, HasFlaggedStateInvariant, "FLAGGED state invariant ihlali (06 §3.5).")
            .Permit(TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN);
    }

    // 06 §3.5 status → zorunlu field matrisi (caller-set alanlar; OnEntry timestamp'leri ayrı).
    private bool HasFieldsForAccepted() =>
        _transaction.BuyerId.HasValue && !string.IsNullOrEmpty(_transaction.BuyerRefundAddress);

    private bool HasFieldsForItemEscrowed() =>
        HasFieldsForAccepted() && !string.IsNullOrEmpty(_transaction.EscrowBotAssetId);

    private bool HasFieldsForItemDelivered() =>
        HasFieldsForItemEscrowed() && !string.IsNullOrEmpty(_transaction.DeliveredBuyerAssetId);

    // FLAGGED state invariant — 06 §3.5 not + 03 §7: tüm deadline + Hangfire job ID NULL.
    private bool HasFlaggedStateInvariant() =>
        _transaction.AcceptDeadline is null
        && _transaction.TradeOfferToSellerDeadline is null
        && _transaction.PaymentDeadline is null
        && _transaction.TradeOfferToBuyerDeadline is null
        && _transaction.PaymentTimeoutJobId is null
        && _transaction.TimeoutWarningJobId is null;
}
