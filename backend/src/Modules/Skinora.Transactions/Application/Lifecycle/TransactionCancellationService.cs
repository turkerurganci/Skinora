using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;
using Skinora.Users.Application.Reputation;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// T51 — 07 §7.7 implementation. All side effects (state transition,
/// timeout-job cancellation, outbox events, reputation recompute,
/// cooldown evaluation) land inside a single
/// <see cref="DbContext.SaveChangesAsync"/> so the active state flip is
/// atomic with the emitted events (09 §13.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Role-aware trigger selection (02 §7):</b> the caller is either the
/// seller or the buyer; the service derives the
/// <see cref="TransactionTrigger"/> from the (role, current state) pair so the
/// state machine fires the correct transition. Sellers in
/// <c>TRADE_OFFER_SENT_TO_SELLER</c> use the dedicated
/// <see cref="TransactionTrigger.SellerDecline"/> trigger because the state
/// machine reserves <see cref="TransactionTrigger.SellerCancel"/> for the
/// pre-trade-offer states only — both end at
/// <see cref="TransactionStatus.CANCELLED_SELLER"/> with identical fields.
/// </para>
/// <para>
/// <b>Post-payment guard (02 §7):</b> states reachable only after
/// <c>PaymentReceivedAt</c> is set (PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER,
/// ITEM_DELIVERED) short-circuit to <c>PAYMENT_ALREADY_SENT</c>. The state
/// machine also permits <see cref="TransactionTrigger.BuyerDecline"/> on
/// TRADE_OFFER_SENT_TO_BUYER, but that path is reserved for the future
/// dispute / admin-driven flow — the user-facing cancel endpoint blocks it.
/// </para>
/// <para>
/// <b>Item return:</b> when the item was on the platform immediately before
/// the cancel transition (<c>EscrowBotAssetId</c> is set, i.e. previous state
/// was <c>ITEM_ESCROWED</c>), the service emits
/// <see cref="ItemRefundToSellerRequestedEvent"/> with a cancel-flavoured
/// <see cref="ItemRefundTrigger"/>. The Steam sidecar consumer (T64–T68)
/// handles the actual return-trade-offer.
/// </para>
/// <para>
/// <b>Reputation + cooldown:</b> after a successful cancel the responsible
/// party's denormalized stats are recomputed via
/// <see cref="IReputationAggregator"/>, and the cooldown rule is re-evaluated
/// via <see cref="IUserCancelCooldownEvaluator"/>. The non-responsible party
/// is recomputed too (their denominator gains a row) but never receives a
/// cooldown stamp because the cooldown evaluator's responsibility map skips
/// non-responsible cancels.
/// </para>
/// </remarks>
public sealed class TransactionCancellationService : ITransactionCancellationService
{
    /// <summary>Minimum trimmed length of <c>reason</c> per 07 §7.7 / 02 §7.</summary>
    public const int MinReasonLength = 10;

    private readonly AppDbContext _db;
    private readonly IOutboxService _outbox;
    private readonly ITimeoutSchedulingService _timeouts;
    private readonly IReputationAggregator _reputation;
    private readonly IUserCancelCooldownEvaluator _cooldown;
    private readonly TimeProvider _clock;

    public TransactionCancellationService(
        AppDbContext db,
        IOutboxService outbox,
        ITimeoutSchedulingService timeouts,
        IReputationAggregator reputation,
        IUserCancelCooldownEvaluator cooldown,
        TimeProvider clock)
    {
        _db = db;
        _outbox = outbox;
        _timeouts = timeouts;
        _reputation = reputation;
        _cooldown = cooldown;
        _clock = clock;
    }

    public async Task<CancelTransactionOutcome> CancelAsync(
        Guid callerUserId,
        Guid transactionId,
        CancelTransactionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ---------- Stage 1: load transaction ----------
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted, cancellationToken);
        if (transaction is null)
            return Failure(CancelTransactionStatus.NotFound,
                TransactionErrorCodes.TransactionNotFound,
                "Transaction not found.");

        // ---------- Stage 2: party guard ----------
        var role = ResolveRole(transaction, callerUserId);
        if (role is null)
            return Failure(CancelTransactionStatus.NotAParty,
                TransactionErrorCodes.NotAParty,
                "Caller is not a party to this transaction.");

        // ---------- Stage 3: reason validation (≥10 chars trimmed) ----------
        var trimmedReason = (request.Reason ?? string.Empty).Trim();
        if (trimmedReason.Length < MinReasonLength)
            return Failure(CancelTransactionStatus.ValidationFailed,
                TransactionErrorCodes.CancelReasonRequired,
                $"reason must be at least {MinReasonLength} characters (07 §7.7).");

        // ---------- Stage 4: state guard + role → trigger mapping ----------
        // 02 §7: post-payment cancel by either party is forbidden. The state
        // machine permits BuyerDecline on TRADE_OFFER_SENT_TO_BUYER, but that
        // path is reserved for the dispute / admin orchestrator (T58 / T59).
        if (IsPostPaymentState(transaction.Status))
            return Failure(CancelTransactionStatus.PaymentAlreadySent,
                TransactionErrorCodes.PaymentAlreadySent,
                "Payment has already been sent; the transaction can no longer be cancelled by either party (02 §7).");

        var trigger = ResolveTrigger(role.Value, transaction.Status);
        if (trigger is null)
            return Failure(CancelTransactionStatus.InvalidStateTransition,
                TransactionErrorCodes.InvalidStateTransition,
                $"Cannot cancel transaction in state {transaction.Status} as {role.Value} (05 §4.2).");

        // ---------- Stage 5: state transition ----------
        // Capture the pre-cancel state up-front: the state machine's OnEntry
        // handlers stamp CancelledAt, so we cannot derive item-return logic
        // from the post-trigger entity.
        var previousStatus = transaction.Status;
        var itemWasOnPlatform = previousStatus == TransactionStatus.ITEM_ESCROWED;

        var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
        try
        {
            machine.Fire(trigger.Value, new CancellationContext(trimmedReason));
        }
        catch (DomainException ex)
        {
            return Failure(CancelTransactionStatus.InvalidStateTransition,
                ex.ErrorCode,
                ex.Message);
        }

        // ---------- Stage 6: side effects ----------
        var occurredAt = _clock.GetUtcNow().UtcDateTime;

        // 6a. Cancel pending Hangfire timeout / warning jobs (idempotent).
        await _timeouts.CancelTimeoutJobsAsync(transaction.Id, cancellationToken);

        // 6b. Item return event when the item was on the platform (i.e. the
        // pre-cancel state was ITEM_ESCROWED). Trigger flavour mirrors role.
        if (itemWasOnPlatform)
        {
            var refundTrigger = role.Value == CancelledByType.SELLER
                ? ItemRefundTrigger.SellerCancel
                : ItemRefundTrigger.BuyerCancel;

            await _outbox.PublishAsync(
                new ItemRefundToSellerRequestedEvent(
                    EventId: Guid.NewGuid(),
                    TransactionId: transaction.Id,
                    SellerId: transaction.SellerId,
                    Trigger: refundTrigger,
                    OccurredAt: occurredAt),
                cancellationToken);
        }

        // 6c. Counter-party notification fan-out.
        await _outbox.PublishAsync(
            new TransactionCancelledEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                CancelledBy: role.Value,
                SellerId: transaction.SellerId,
                BuyerId: transaction.BuyerId,
                ItemName: transaction.ItemName,
                CancelReason: trimmedReason,
                OccurredAt: occurredAt),
            cancellationToken);

        // ---------- Stage 7: atomic commit + denormalized projection update ----------
        // The reputation aggregator + cooldown evaluator both query Transaction
        // rows with AsNoTracking, so they cannot observe the in-flight cancel
        // until it is flushed. Wrap both writes in a single DB transaction so
        // (state flip + outbox events + reputation/cooldown updates) commit or
        // roll back together — atomicity boundary 09 §13.3.
        await using var dbTx = await _db.Database.BeginTransactionAsync(cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        // 7a. Recompute denormalized reputation for both parties (when present).
        // The aggregator's responsibility map handles "who counts what"; calling
        // it for both parties keeps the denominator fresh on the non-responsible
        // side too. Buyer may be null (pre-accept seller cancel) — skip in that
        // case.
        await _reputation.RecomputeAsync(transaction.SellerId, cancellationToken);
        if (transaction.BuyerId is { } buyerId)
            await _reputation.RecomputeAsync(buyerId, cancellationToken);

        // 7b. Re-evaluate cooldown for the responsible party only — the
        // cooldown rule applies to the user who initiated the cancel
        // (CANCELLED_SELLER → seller; CANCELLED_BUYER → buyer; non-responsible
        // counter-parties are filtered out inside the evaluator).
        await _cooldown.EvaluateAsync(callerUserId, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await dbTx.CommitAsync(cancellationToken);

        return new CancelTransactionOutcome(
            CancelTransactionStatus.Cancelled,
            new CancelTransactionResponse(
                Status: transaction.Status,
                CancelledAt: transaction.CancelledAt!.Value,
                ItemReturned: itemWasOnPlatform,
                // T51 user-cancel can never reach a post-payment state, so
                // the buyer's payment is never in flight here. Admin-cancel
                // (T59) and timeout-cancel (T49) own the refund path.
                PaymentRefunded: false),
            ErrorCode: null,
            ErrorMessage: null);
    }

    private static CancelledByType? ResolveRole(Transaction transaction, Guid callerUserId)
    {
        if (transaction.SellerId == callerUserId)
            return CancelledByType.SELLER;
        if (transaction.BuyerId == callerUserId)
            return CancelledByType.BUYER;
        return null;
    }

    private static bool IsPostPaymentState(TransactionStatus status) => status switch
    {
        TransactionStatus.PAYMENT_RECEIVED => true,
        TransactionStatus.TRADE_OFFER_SENT_TO_BUYER => true,
        TransactionStatus.ITEM_DELIVERED => true,
        _ => false,
    };

    /// <summary>
    /// Maps (role × state) → state-machine trigger per 05 §4.2 / 02 §7.
    /// Returning <c>null</c> means the user-cancel endpoint refuses the
    /// transition (terminal states, FLAGGED, COMPLETED, etc.).
    /// </summary>
    private static TransactionTrigger? ResolveTrigger(CancelledByType role, TransactionStatus status)
        => (role, status) switch
        {
            (CancelledByType.SELLER, TransactionStatus.CREATED) => TransactionTrigger.SellerCancel,
            (CancelledByType.SELLER, TransactionStatus.ACCEPTED) => TransactionTrigger.SellerCancel,
            // 05 §4.2: SellerCancel is not permitted at TRADE_OFFER_SENT_TO_SELLER;
            // SellerDecline carries identical CancelledByType=SELLER + reason
            // semantics (06 §3.5) and ends at the same CANCELLED_SELLER state.
            (CancelledByType.SELLER, TransactionStatus.TRADE_OFFER_SENT_TO_SELLER) => TransactionTrigger.SellerDecline,
            (CancelledByType.SELLER, TransactionStatus.ITEM_ESCROWED) => TransactionTrigger.SellerCancel,

            (CancelledByType.BUYER, TransactionStatus.CREATED) => TransactionTrigger.BuyerCancel,
            (CancelledByType.BUYER, TransactionStatus.ACCEPTED) => TransactionTrigger.BuyerCancel,
            (CancelledByType.BUYER, TransactionStatus.TRADE_OFFER_SENT_TO_SELLER) => TransactionTrigger.BuyerCancel,
            (CancelledByType.BUYER, TransactionStatus.ITEM_ESCROWED) => TransactionTrigger.BuyerCancel,

            _ => null,
        };

    private static CancelTransactionOutcome Failure(
        CancelTransactionStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);
}
