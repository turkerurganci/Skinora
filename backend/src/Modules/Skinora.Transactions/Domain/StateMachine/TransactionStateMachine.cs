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

        // Kalan süreyi, o state'te hangi deadline aktifse ondan hesapla
        // (06 §3.5 state → aktif deadline matrisi). Teslimat fazı bu listeye
        // v3.0'da eklendi: eskiden DeliveryDeadline ileri akışta hiç
        // armlanmadığı için dondurulacak bir süre de yoktu.
        var activeDeadline = _transaction.Status switch
        {
            TransactionStatus.SELLER_CONFIRMED => _transaction.PaymentDeadline,
            TransactionStatus.PAYMENT_RECEIVED => _transaction.DeliveryDeadline,
            _ => null,
        };

        if (activeDeadline.HasValue)
        {
            var remaining = (activeDeadline.Value - now).TotalSeconds;
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

            // 02 §4.5.1 — the settlement re-check found the item gone from the
            // buyer's inventory, so the trade was reversed. Attributed to the
            // SELLER: reversal-after-payout is the documented seller-fraud path
            // and T129 raises the fraud flag on the same side. Without an
            // attribution the REFUNDED row would violate CK_Transactions_Cancel,
            // which requires the full (CancelledBy, CancelReason, CancelledAt)
            // trail on every refund/cancel terminal state.
            TransactionTrigger.DeliveryReversed => CancelledByType.SELLER,
            TransactionTrigger.BuyerCancel => CancelledByType.BUYER,
            TransactionTrigger.AdminCancel or TransactionTrigger.AdminReject
                or TransactionTrigger.AdminResolveRefund => CancelledByType.ADMIN,
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
            TransactionTrigger.DeliveryReversed =>
                "Mutabakat kontrolünde item alıcının envanterinde bulunamadı — trade geri alınmış (02 §4.5.1)",
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
            .PermitIf(TransactionTrigger.BuyerAccept, TransactionStatus.ACCEPTED, HasFieldsForAccepted, "BuyerId, BuyerRefundAddress ve BuyerTradeUrl zorunlu (06 §3.5).")
            .Permit(TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT)
            .Permit(TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER)
            .Permit(TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER)
            .Permit(TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN);

        // ACCEPTED — satıcının hazırlık onayı bekleniyor (03 §2.3).
        // Timeout burada SATICIYA yazılır: onayı vermeyen taraf odur.
        _machine.Configure(TransactionStatus.ACCEPTED)
            .OnEntry(() => _transaction.AcceptedAt = DateTime.UtcNow)
            .PermitIf(TransactionTrigger.SellerConfirmReady, TransactionStatus.SELLER_CONFIRMED, HasFieldsForSellerConfirmed, "SellerReadyConfirmedAt zorunlu (06 §3.5).")
            .Permit(TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT)
            .Permit(TransactionTrigger.SellerDecline, TransactionStatus.CANCELLED_SELLER)
            .Permit(TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER)
            .Permit(TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER)
            .Permit(TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN);

        // SELLER_CONFIRMED — ödeme bekleniyor (timeout warning aktif).
        // Item satıcıda durmaya devam ediyor; platform hiçbir şey tutmuyor.
        _machine.Configure(TransactionStatus.SELLER_CONFIRMED)
            .OnEntry(() => _transaction.TimeoutWarningSentAt = null)
            .OnExit(() =>
            {
                _transaction.TimeoutWarningJobId = null;
                _transaction.TimeoutWarningSentAt = null;
            })
            .Permit(TransactionTrigger.ConfirmPayment, TransactionStatus.PAYMENT_RECEIVED)
            .Permit(TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT)

            // 05 §4.2 guards BOTH cancels here on `PaymentReceivedAt is null`.
            // The state should make that redundant (the field is stamped on
            // entry to PAYMENT_RECEIVED), which is exactly why it is worth
            // asserting: if a payment is ever recorded without the state
            // advancing, neither party may cancel the money away from here.
            .PermitIf(TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER, () => _transaction.PaymentReceivedAt is null, "Ödeme kaydedilmiş bir işlem bu durumdan iptal edilemez (05 §4.2).")
            .PermitIf(TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER, () => _transaction.PaymentReceivedAt is null, "Ödeme yapıldıktan sonra alıcı iptal edemez (02 §7).")
            .Permit(TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN)
            .Permit(TransactionTrigger.AdminResolveRefund, TransactionStatus.REFUNDED);

        // PAYMENT_RECEIVED — para emanette; satıcı item'ı DOĞRUDAN alıcıya
        // gönderiyor. Platform trade'in tarafı değil, yalnızca sonucu gözlüyor.
        //
        // İptal yetkisi burada asimetrik (02 §7): satıcı vazgeçebilir, alıcı
        // vazgeçemez. Satıcının yolu kapatılsaydı göndermek istemeyen satıcı
        // hiçbir şey yapmayıp timeout'u beklerdi — alıcı parasına daha geç
        // kavuşurdu. Açık bırakmak kaçınılmaz sonucu hızlandırıyor.
        _machine.Configure(TransactionStatus.PAYMENT_RECEIVED)
            .OnEntry(() => _transaction.PaymentReceivedAt = DateTime.UtcNow)
            .PermitIf(TransactionTrigger.DeliverItem, TransactionStatus.ITEM_DELIVERED, HasDeliveryEntryInvariant, "Teslimat kanıtı yetersiz veya mutabakat penceresi açılmamış (02 §9.2, §4.5.1).")
            .Permit(TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT)
            .Permit(TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER)
            .Permit(TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN)
            .Permit(TransactionTrigger.AdminResolveRefund, TransactionStatus.REFUNDED);

        // ITEM_DELIVERED — mutabakat süresi (02 §4.5.1). Steam korumalı bir
        // trade'i 7 gün boyunca geri alınabilir tutuyor ve bunu trade'in her
        // iki tarafı da Steam Support'a başvurmadan yapabiliyor. Bu yüzden
        // ödeme beklemek zorunda; ama asıl korumayı bekleme değil, sürenin
        // SONUNDAKİ kontrol sağlıyor — Complete guard'ı buna bakıyor.
        //
        // Standart admin-cancel bu state'te kullanılamaz (05 §4.2).
        _machine.Configure(TransactionStatus.ITEM_DELIVERED)
            .OnEntry(() => _transaction.ItemDeliveredAt = DateTime.UtcNow)
            .PermitIf(TransactionTrigger.Complete, TransactionStatus.COMPLETED, HasSettlementClearance, "Mutabakat doğrulanmadan ödeme yapılamaz (02 §4.5.1).")
            .Permit(TransactionTrigger.DeliveryReversed, TransactionStatus.REFUNDED)
            .Permit(TransactionTrigger.AdminResolveRefund, TransactionStatus.REFUNDED);

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

        // REFUNDED — terminal (WP5 buyer-favor dispute resolution). Reuses the
        // cancellation fields (CancelledBy=ADMIN, CancelReason, CancelledAt) so
        // CK_Transactions_Cancel holds; OnEntry stamps CancelledAt like CANCELLED_*.
        _machine.Configure(TransactionStatus.REFUNDED)
            .OnEntry(() => _transaction.CancelledAt = DateTime.UtcNow);

        // FLAGGED — yalnızca işlem oluşturma anında set edilir (05 §4.2 not).
        // Admin onayında CREATED'a, reddinde CANCELLED_ADMIN'e geçer (03 §7.1).
        _machine.Configure(TransactionStatus.FLAGGED)
            .PermitIf(TransactionTrigger.AdminApprove, TransactionStatus.CREATED, HasFlaggedStateInvariant, "FLAGGED state invariant ihlali (06 §3.5).")
            .PermitIf(TransactionTrigger.AdminReject, TransactionStatus.CANCELLED_ADMIN, HasFlaggedStateInvariant, "FLAGGED state invariant ihlali (06 §3.5).")
            .Permit(TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN);
    }

    // 06 §3.5 status → zorunlu field matrisi (caller-set alanlar; OnEntry timestamp'leri ayrı).
    // T119a: BuyerTradeUrl da bu kümededir — 06 §3.5 alanı ACCEPTED ve
    // sonrasında NOT NULL sayar. DB tarafında CHECK yok (kolon nullable, çünkü
    // CREATED'da doldurulamaz), dolayısıyla invariantı uygulama katmanında
    // zorlayan tek yer burasıdır.
    private bool HasFieldsForAccepted() =>
        _transaction.BuyerId.HasValue
        && !string.IsNullOrEmpty(_transaction.BuyerRefundAddress)
        && !string.IsNullOrEmpty(_transaction.BuyerTradeUrl);

    private bool HasFieldsForSellerConfirmed() =>
        HasFieldsForAccepted() && _transaction.SellerReadyConfirmedAt.HasValue;

    /// <summary>
    /// 02 §9.2 — teslimat kanıtı. Bilinçli olarak <c>DeliveredBuyerAssetId</c>'ye
    /// BAKMAZ: alıcı onayıyla kapanan bir teslimatta envanter hiç okunmamış
    /// olabilir ve o alan null kalır. Kanıtın kendisi <c>DeliveryEvidence</c>.
    /// </summary>
    private bool HasDeliveryEvidence() =>
        HasFieldsForSellerConfirmed()
        && _transaction.DeliveryEvidence.IsSufficientForDelivery()
        && _transaction.DeliveryVerifiedAt.HasValue;

    /// <summary>
    /// ITEM_DELIVERED giriş invariantı (T129): teslimat kanıtı <b>ve</b>
    /// mutabakat penceresi.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 06 §3.5 <c>PayoutEligibleAt</c>'ı ITEM_DELIVERED için zorunlu sayar ve
    /// bunun bir kayıt kuralı değil para kuralı olduğu iki kez pahalıya
    /// öğrenildi: kolonu kimse yazmadığı sürece <c>SellerPayoutQueueJob</c>
    /// hiçbir şey kuyruğa almaz (fail-closed, T126 bulgu F1), ama kolon
    /// olmadan duruma girilebiliyorsa bir gün başka bir yazar onu
    /// <c>ItemDeliveredAt</c>'ten bağımsız doldurur ve pencere sessizce kısalır.
    /// </para>
    /// <para>
    /// Bu yüzden pencere burada, geçişin kendisinde talep ediliyor:
    /// <c>DeliverItem</c> çağıranı <c>SettlementWindowStamper</c>'ı atlarsa
    /// teslimat hiç gerçekleşmez — kapı, koruduğu değerin bütün yazarlarını
    /// denetler (T124/T126 kalıcı dersi).
    /// </para>
    /// </remarks>
    private bool HasDeliveryEntryInvariant() =>
        HasDeliveryEvidence() && _transaction.PayoutEligibleAt.HasValue;

    /// <summary>
    /// 02 §4.5.1 — ödeme ancak mutabakat süresi dolduktan <b>ve</b> item'ın hâlâ
    /// alıcıda olduğu doğrulandıktan sonra yapılabilir.
    /// <para>
    /// Sürenin dolmuş olması tek başına yeterli değildir: beklemek, geri alma
    /// penceresinin kapanmasını sağlar ama geri alınıp alınmadığını söylemez.
    /// Onu söyleyen <c>SettlementVerifiedAt</c>'tir.
    /// </para>
    /// </summary>
    private bool HasSettlementClearance() =>
        _transaction.SettlementVerifiedAt.HasValue
        && _transaction.DeliveryReversedAt is null;

    // FLAGGED state invariant — 06 §3.5 not + 03 §7: tüm deadline + Hangfire job ID NULL.
    private bool HasFlaggedStateInvariant() =>
        _transaction.AcceptDeadline is null
        && _transaction.SellerConfirmDeadline is null
        && _transaction.PaymentDeadline is null
        && _transaction.DeliveryDeadline is null
        && _transaction.PaymentTimeoutJobId is null
        && _transaction.TimeoutWarningJobId is null;
}
