using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Application.PostCancel;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;
using Skinora.Users.Application.Reputation;
using Skinora.Users.Domain.Entities;

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
/// state machine fires the correct transition. A seller cancelling from
/// <c>ACCEPTED</c> may use either <see cref="TransactionTrigger.SellerCancel"/>
/// or <see cref="TransactionTrigger.SellerDecline"/> — both end at
/// <see cref="TransactionStatus.CANCELLED_SELLER"/> with identical fields.
/// </para>
/// <para>
/// <b>Post-payment guard (02 §7, 07 §7.7):</b> asymmetric in v3.0. The
/// <b>buyer</b> is refused once their money is in escrow (PAYMENT_RECEIVED,
/// ITEM_DELIVERED short-circuit to <c>PAYMENT_ALREADY_SENT</c>); the
/// <b>seller</b> may still back out of PAYMENT_RECEIVED, which refunds the
/// buyer. Closing the seller's path would not protect anyone — they would
/// simply let the delivery deadline lapse for the same outcome, later.
/// </para>
/// <para>
/// <b>Payment refund:</b> when the pre-cancel state was PAYMENT_RECEIVED the
/// service emits <see cref="PaymentRefundToBuyerRequestedEvent"/>. There is no
/// item-return counterpart: in the P2P model the item never left the seller's
/// inventory (02 §9).
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
    private readonly IPostCancelMonitorStarter _postCancelMonitor;
    private readonly TimeProvider _clock;

    public TransactionCancellationService(
        AppDbContext db,
        IOutboxService outbox,
        ITimeoutSchedulingService timeouts,
        IReputationAggregator reputation,
        IUserCancelCooldownEvaluator cooldown,
        IPostCancelMonitorStarter postCancelMonitor,
        TimeProvider clock)
    {
        _db = db;
        _outbox = outbox;
        _timeouts = timeouts;
        _reputation = reputation;
        _cooldown = cooldown;
        _postCancelMonitor = postCancelMonitor;
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

        // ---------- Stage 2a: suspension guard (T105a, 02 §14.0) ----------
        // A suspended user cannot take the cancel action. From PAYMENT_RECEIVED
        // this would otherwise publish PaymentRefundToBuyerRequestedEvent and
        // move funds out on a suspended party's initiative. Under the
        // restricted-session model the caller's pending steps fall to timeout
        // instead; admin can drive the lifecycle via the hold/cancel orchestrator.
        var caller = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == callerUserId, cancellationToken);
        if (caller is { IsSuspended: true })
            return Failure(CancelTransactionStatus.AccountSuspended,
                TransactionErrorCodes.AccountSuspended,
                "Your account is suspended; this action is not permitted (02 §14.0).");

        // ---------- Stage 3: reason validation (≥10 chars trimmed) ----------
        var trimmedReason = (request.Reason ?? string.Empty).Trim();
        if (trimmedReason.Length < MinReasonLength)
            return Failure(CancelTransactionStatus.ValidationFailed,
                TransactionErrorCodes.CancelReasonRequired,
                $"reason must be at least {MinReasonLength} characters (07 §7.7).");

        // ---------- Stage 4: state guard + role → trigger mapping ----------
        // 02 §7 / 07 §7.7 — post-payment cancel is asymmetric in v3.0: the
        // BUYER is refused once their money is in escrow, the SELLER is not.
        // The role check is load-bearing; applying this guard to both parties
        // would make the seller's PAYMENT_RECEIVED branch in ResolveTrigger
        // unreachable and silently reinstate the pre-pivot rule.
        if (role.Value == CancelledByType.BUYER && IsPostPaymentState(transaction.Status))
            return Failure(CancelTransactionStatus.PaymentAlreadySent,
                TransactionErrorCodes.PaymentAlreadySent,
                "Payment has already been sent; the buyer can no longer cancel the transaction (02 §7).");

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

        // WP15 — audit-trail row (06 §3.6). The cancelling party is the actor
        // (USER). Reputation + cooldown are recomputed inline in Stage 7 below;
        // CANCELLED_SELLER/BUYER attribution reads the resulting Status directly,
        // so this history row is for the audit trail (06 §3.6), not the formula.
        TransactionHistoryRecorder.Record(
            _db, transaction, previousStatus, trigger.Value,
            ActorType.USER, callerUserId, occurredAt);

        // 6a. Cancel pending Hangfire timeout / warning jobs (idempotent).
        await _timeouts.CancelTimeoutJobsAsync(transaction.Id, cancellationToken);

        // 6b. No item-return event exists in the P2P model — the item never
        // left the seller's inventory, so a cancellation only ever moves money
        // (02 §9). Payment refund, when the buyer had already paid:
        if (previousStatus == TransactionStatus.PAYMENT_RECEIVED
            && transaction.BuyerId is { } buyerForRefund
            && !string.IsNullOrWhiteSpace(transaction.BuyerRefundAddress))
        {
            await _outbox.PublishAsync(
                new PaymentRefundToBuyerRequestedEvent(
                    EventId: Guid.NewGuid(),
                    TransactionId: transaction.Id,
                    BuyerId: buyerForRefund,
                    BuyerRefundAddress: transaction.BuyerRefundAddress!,
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
                FromStatus: previousStatus,
                OccurredAt: occurredAt),
            cancellationToken);

        // 6d. T75 — stamp PaymentAddress for post-cancel monitoring + queue
        // the sidecar start event. Idempotent on transactions without a
        // PaymentAddress (CREATED-cancel before allocation).
        await _postCancelMonitor.RequestStartAsync(transaction.Id, occurredAt, cancellationToken);

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
                // v3.0 — a seller cancel from PAYMENT_RECEIVED does refund the
                // buyer (02 §7), so this is no longer always false.
                PaymentRefunded: previousStatus == TransactionStatus.PAYMENT_RECEIVED),
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

    // Post-payment states, used to refuse the BUYER's cancel (07 §7.7 gives
    // 422 PAYMENT_ALREADY_SENT for the buyer only). The seller is not blocked
    // here — see ResolveTrigger (02 §7). The seller's own refusal at
    // ITEM_DELIVERED comes from ResolveTrigger returning null → 409.
    private static bool IsPostPaymentState(TransactionStatus status) => status switch
    {
        TransactionStatus.PAYMENT_RECEIVED => true,
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
            (CancelledByType.SELLER, TransactionStatus.SELLER_CONFIRMED) => TransactionTrigger.SellerCancel,

            // v3.0 — the seller may still back out after the buyer has paid
            // (02 §7). Closing this would not protect the buyer: the seller
            // would simply let the delivery deadline lapse and the buyer would
            // wait longer for the same refund.
            (CancelledByType.SELLER, TransactionStatus.PAYMENT_RECEIVED) => TransactionTrigger.SellerCancel,

            (CancelledByType.BUYER, TransactionStatus.CREATED) => TransactionTrigger.BuyerCancel,
            (CancelledByType.BUYER, TransactionStatus.ACCEPTED) => TransactionTrigger.BuyerCancel,
            (CancelledByType.BUYER, TransactionStatus.SELLER_CONFIRMED) => TransactionTrigger.BuyerCancel,

            // The buyer has no PAYMENT_RECEIVED entry: once their money is in
            // escrow they cannot unilaterally cancel (02 §7).
            _ => null,
        };

    private static CancelTransactionOutcome Failure(
        CancelTransactionStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);
}
