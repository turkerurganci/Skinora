using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Transactions.Application.Settlement;

/// <summary>
/// T129 — the end-of-window check that turns a waited-out settlement into a
/// payout or a refund (02 §4.5.1, 03 §2.4 step 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this job exists at all.</b> Waiting eight days protects nothing on its
/// own; what protects is the re-read at the end of the window. Until this job
/// stamps <c>SettlementVerifiedAt</c>, the COMPLETED guard refuses to advance
/// and <see cref="Transfers.SellerPayoutQueueJob"/> queues nothing — so the
/// entire settlement mechanism is inert without it, in the safe direction.
/// </para>
/// <para>
/// <b>What it may and may not decide.</b> Two of the four verdicts move money's
/// direction (verified → payout, reversal → refund) and the other two park the
/// transaction for an admin. That asymmetry is the point: a wrong "verified"
/// pays a seller who took the item back, and a wrong "reversed" refunds a buyer
/// who simply sold the skin on — while a wrong "escalate" only costs a human
/// five minutes. Everything the platform cannot establish therefore lands on the
/// escalation path (owner decision, 2026-08-16).
/// </para>
/// <para>
/// <b>Fairness of the queue.</b> Candidates are ordered by
/// <c>SettlementCheckedAt</c> (nulls first) rather than by
/// <c>PayoutEligibleAt</c>. A row whose inventory cannot be read stays eligible
/// forever; ordering by eligibility date would let the oldest unreadable rows
/// refill every batch and starve settlements that came due today — the same
/// starvation T127 found in the delivery scanner.
/// </para>
/// </remarks>
public sealed class SettlementVerificationJob
{
    public const string RecurringJobId = "settlement-verification";

    /// <summary>
    /// Cron — every five minutes. The window this job closes is measured in
    /// days, so minute-level granularity buys nothing, while each candidate
    /// costs one or two rate-limited Steam reads (08 §2.3). Deliberately slower
    /// than the per-minute transfer jobs for that reason.
    /// </summary>
    public const string Cron = "*/5 * * * *";

    /// <summary>
    /// Batch size — smaller than the transfer jobs' 20 because every row here
    /// spends Steam reads rather than database work.
    /// </summary>
    public const int BatchSize = 10;

    public const int ConcurrencyLockTimeoutSeconds = 280;

    /// <summary>
    /// How often a transaction that has ALREADY been escalated is re-checked.
    /// </summary>
    /// <remarks>
    /// Re-checking an escalated row still has a point — an unreadable inventory
    /// may open, and then the platform resolves what a human would otherwise
    /// have to — but a human is already looking, so it does not need to happen
    /// at the batch cadence. Steam reads are rate-limited (08 §2.3, T120), and
    /// without this throttle a handful of permanently ambiguous rows would spend
    /// two reads each, twelve times an hour, forever.
    /// </remarks>
    public static readonly TimeSpan EscalatedRecheckInterval = TimeSpan.FromHours(1);

    private readonly AppDbContext _db;
    private readonly ISettlementVerificationService _verification;
    private readonly ISettlementSettingsProvider _settings;
    private readonly ITransactionFraudFlagWriter _flagWriter;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<SettlementVerificationJob> _logger;

    public SettlementVerificationJob(
        AppDbContext db,
        ISettlementVerificationService verification,
        ISettlementSettingsProvider settings,
        ITransactionFraudFlagWriter flagWriter,
        IOutboxService outbox,
        TimeProvider clock,
        ILogger<SettlementVerificationJob> logger)
    {
        _db = db;
        _verification = verification;
        _settings = settings;
        _flagWriter = flagWriter;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var settings = await _settings.GetAsync(cancellationToken);

        // Soft-delete query filter excludes IsDeleted rows. A held or disputed
        // transaction is skipped exactly as in the payout/sweep jobs: 03 §2.4
        // step 1 says a dispute opened during the window blocks the payment and
        // waits for the dispute's own outcome, and a hold freezes everything.
        var escalatedRecheckBefore = nowUtc - EscalatedRecheckInterval;

        var candidateIds = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.Status == TransactionStatus.ITEM_DELIVERED
                && !t.IsOnHold
                && !t.HasActiveDispute
                && t.SettlementVerifiedAt == null
                && t.DeliveryReversedAt == null
                && t.PayoutEligibleAt != null
                && t.PayoutEligibleAt <= nowUtc
                // Already with an admin → re-checked hourly rather than every
                // tick (see EscalatedRecheckInterval).
                && (t.SettlementEscalatedAt == null
                    || t.SettlementCheckedAt == null
                    || t.SettlementCheckedAt <= escalatedRecheckBefore))
            .OrderBy(t => t.SettlementCheckedAt == null ? 0 : 1)
            .ThenBy(t => t.SettlementCheckedAt)
            .ThenBy(t => t.PayoutEligibleAt)
            .Take(BatchSize)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0) return;

        _logger.LogInformation(
            "SettlementVerificationJob picked up {Count} transactions whose settlement window has closed",
            candidateIds.Count);

        foreach (var id in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessAsync(id, settings, cancellationToken);
        }
    }

    private async Task ProcessAsync(
        Guid transactionId,
        SettlementSettings settings,
        CancellationToken cancellationToken)
    {
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        // Re-validate inside the loop (09 §13.3): a concurrent admin hold,
        // dispute, resolution or a pushed-out window must not be overwritten by
        // a stale tick that selected this row before the write.
        if (transaction is null
            || transaction.Status != TransactionStatus.ITEM_DELIVERED
            || transaction.IsOnHold
            || transaction.HasActiveDispute
            || transaction.SettlementVerifiedAt is not null
            || transaction.DeliveryReversedAt is not null
            || transaction.PayoutEligibleAt is not { } eligibleAt
            || eligibleAt > nowUtc)
        {
            return;
        }

        SettlementVerificationResult result;
        try
        {
            result = await _verification.VerifyAsync(transaction, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A read that threw is absence of information, not a finding. Stamp
            // the round so the queue rotates and try again next tick.
            _logger.LogWarning(ex,
                "Transaction {TransactionId}: settlement verification threw — treated as inconclusive",
                transaction.Id);
            transaction.SettlementCheckedAt = nowUtc;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Written on every arm, including the ones that conclude nothing: this
        // is the column the batch ordering depends on.
        transaction.SettlementCheckedAt = nowUtc;

        switch (result.Verdict)
        {
            case SettlementVerdict.Verified:
                await ClearForPayoutAsync(transaction, result, nowUtc, cancellationToken);
                break;

            case SettlementVerdict.ReversalSignature when settings.ReversalAutoRefundEnabled:
                await ApplyReversalAsync(transaction, result, nowUtc, cancellationToken);
                break;

            case SettlementVerdict.ReversalSignature:
                await EscalateAsync(
                    transaction, SettlementReviewReasons.ReversalGated, result, nowUtc, cancellationToken);
                break;

            case SettlementVerdict.AmbiguousDeparture:
                await EscalateAsync(
                    transaction, SettlementReviewReasons.AmbiguousDeparture, result, nowUtc, cancellationToken);
                break;

            case SettlementVerdict.Inconclusive:
            default:
                await HandleInconclusiveAsync(transaction, result, settings, nowUtc, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// The item is still with the buyer: stamp the clearance the COMPLETED
    /// guard reads, and let <c>SellerPayoutQueueJob</c> queue the payout.
    /// </summary>
    private async Task ClearForPayoutAsync(
        Transaction transaction,
        SettlementVerificationResult result,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        transaction.SettlementVerifiedAt = nowUtc;

        _logger.LogInformation(
            "Transaction {TransactionId}: settlement verified — {Detail}",
            transaction.Id, result.Detail);

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Reversal proven and the gate open: refund the buyer, refuse the seller's
    /// payout, flag the seller's account (02 §4.5.1).
    /// </summary>
    private async Task ApplyReversalAsync(
        Transaction transaction,
        SettlementVerificationResult result,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var previousStatus = transaction.Status;

        // Stamped before the trigger so the transaction can never be observed in
        // REFUNDED without the column that says WHY it refunded — and so the
        // COMPLETED guard's second half (DeliveryReversedAt is null) is already
        // false if anything races us.
        transaction.DeliveryReversedAt = nowUtc;

        var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
        try
        {
            machine.Fire(TransactionTrigger.DeliveryReversed);
        }
        catch (DomainException ex)
        {
            transaction.DeliveryReversedAt = null;
            _logger.LogError(ex,
                "Transaction {TransactionId}: settlement found a reversal but DeliveryReversed was "
                + "refused ({ErrorCode}) — the stamp was rolled back and the transaction stays in {Status}",
                transaction.Id, ex.ErrorCode, transaction.Status);

            // The round itself still happened; keep the ordering stamp.
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        // WP15 — audit-trail row (06 §3.6). SYSTEM actor: this conclusion is the
        // platform's own inference, not a user or admin action.
        TransactionHistoryRecorder.Record(
            _db, transaction, previousStatus, TransactionTrigger.DeliveryReversed,
            ActorType.SYSTEM, SeedConstants.SystemUserId, nowUtc);

        // The money side. Same event the timeout/admin-cancel refund paths use,
        // so the buyer refund travels the one audited transfer pipeline (WP2)
        // rather than a second one invented here.
        if (transaction.BuyerId is { } buyerId && !string.IsNullOrEmpty(transaction.BuyerRefundAddress))
        {
            await _outbox.PublishAsync(
                new PaymentRefundToBuyerRequestedEvent(
                    EventId: Guid.NewGuid(),
                    TransactionId: transaction.Id,
                    BuyerId: buyerId,
                    BuyerRefundAddress: transaction.BuyerRefundAddress,
                    OccurredAt: nowUtc),
                cancellationToken);
        }
        else
        {
            // Structurally unreachable (06 §3.5 requires both fields from
            // ACCEPTED onward), but a delivered transaction without a refund
            // address would silently keep the buyer's money — say so loudly.
            _logger.LogError(
                "Transaction {TransactionId}: reversal detected but the buyer refund could not be "
                + "requested (BuyerId={BuyerId}, refund address set={HasAddress}) — admin action required",
                transaction.Id, transaction.BuyerId,
                !string.IsNullOrEmpty(transaction.BuyerRefundAddress));
        }

        // 02 §4.5.1 — "satıcı hesabına dolandırıcılık işareti konur". Account
        // level, so §14.2 can count the repeat (06 §3.12: ACCOUNT_LEVEL rows
        // carry no TransactionId, hence the id in the payload).
        await _flagWriter.StageAccountFlagAsync(
            transaction.SellerId,
            FraudFlagType.DELIVERY_REVERSED,
            JsonSerializer.Serialize(new
            {
                transactionId = transaction.Id,
                itemName = transaction.ItemName,
                itemAssetId = transaction.ItemAssetId,
                deliveredBuyerAssetId = transaction.DeliveredBuyerAssetId,
                itemDeliveredAt = transaction.ItemDeliveredAt,
                payoutEligibleAt = transaction.PayoutEligibleAt,
                detectedAt = nowUtc,
                buyerVisibility = result.BuyerVisibility?.ToString(),
                sellerVisibility = result.SellerVisibility?.ToString(),
                observedClassCount = result.ObservedClassCount,
                expectedClassCount = result.ExpectedClassCount,
                detail = result.Detail,
            }),
            cancellationToken);

        await _outbox.PublishAsync(
            new SettlementReversalDetectedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                SellerId: transaction.SellerId,
                BuyerId: transaction.BuyerId ?? Guid.Empty,
                ItemName: transaction.ItemName,
                OccurredAt: nowUtc),
            cancellationToken);

        // Realtime relay (WP9) — the parties' screens must not keep showing a
        // settlement countdown for a transaction that has refunded.
        await _outbox.PublishAsync(
            new TransactionStatusChangedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                FromStatus: previousStatus,
                ToStatus: transaction.Status,
                OccurredAt: nowUtc),
            cancellationToken);

        _logger.LogWarning(
            "Transaction {TransactionId}: trade reversal confirmed at settlement — REFUNDED, "
            + "seller {SellerId} flagged (02 §4.5.1). {Detail}",
            transaction.Id, transaction.SellerId, result.Detail);

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Inconclusive: retry silently until the configured threshold, then ask a
    /// human. The payout stays parked throughout — the threshold decides WHEN
    /// somebody is told, never whether the money moves.
    /// </summary>
    private async Task HandleInconclusiveAsync(
        Transaction transaction,
        SettlementVerificationResult result,
        SettlementSettings settings,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var overdueSince = transaction.PayoutEligibleAt ?? nowUtc;
        var stuckFor = nowUtc - overdueSince;

        if (stuckFor >= TimeSpan.FromHours(settings.UnreadableEscalationHours))
        {
            await EscalateAsync(
                transaction, SettlementReviewReasons.Unreadable, result, nowUtc, cancellationToken);
            return;
        }

        _logger.LogInformation(
            "Transaction {TransactionId}: settlement check inconclusive ({Detail}) — retrying; "
            + "{Hours:F1}h of {Threshold}h before escalation",
            transaction.Id, result.Detail, stuckFor.TotalHours, settings.UnreadableEscalationHours);

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Hand the transaction to an admin. Idempotent on
    /// <see cref="Transaction.SettlementEscalatedAt"/>: the admin is told once,
    /// not once per tick, and the transaction keeps being re-checked in case the
    /// inventory becomes readable before anyone acts.
    /// </summary>
    private async Task EscalateAsync(
        Transaction transaction,
        string reason,
        SettlementVerificationResult result,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (transaction.SettlementEscalatedAt is not null)
        {
            _logger.LogDebug(
                "Transaction {TransactionId}: settlement already escalated at {EscalatedAt} — "
                + "re-checked ({Reason}), no second notification",
                transaction.Id, transaction.SettlementEscalatedAt, reason);
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        transaction.SettlementEscalatedAt = nowUtc;

        await _outbox.PublishAsync(
            new SettlementReviewRequiredEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                SellerId: transaction.SellerId,
                BuyerId: transaction.BuyerId,
                Reason: reason,
                Detail: result.Detail,
                OccurredAt: nowUtc),
            cancellationToken);

        _logger.LogWarning(
            "Transaction {TransactionId}: settlement escalated to admin ({Reason}) — {Detail}",
            transaction.Id, reason, result.Detail);

        await _db.SaveChangesAsync(cancellationToken);
    }

    // Hangfire serializes Expression<Action<T>>; expose a sync wrapper.
    // [DisableConcurrentExecution] keeps two ticks from re-reading and
    // re-deciding the same rows — the reads are rate-limited and the decisions
    // move money.
    [DisableConcurrentExecution(ConcurrencyLockTimeoutSeconds)]
    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
