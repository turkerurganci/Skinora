using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// WP1 producer leg — per-minute Hangfire job that closes the post-delivery
/// gap in the escrow happy path (03 §2.4, PRE_F6_PLAN WP1). A transaction
/// that reaches ITEM_DELIVERED has no other path forward: the only permitted
/// trigger is <c>Complete</c>, which fires once the seller payout confirms on
/// chain. This job creates the missing PENDING <c>SELLER_PAYOUT</c>
/// <c>BlockchainTransaction</c> row; the existing
/// <see cref="OutgoingTransferDispatchJob"/> then broadcasts it and
/// <see cref="OutgoingTransferConfirmationJob"/> confirms it.
///
/// <para>
/// The payout amount is the gas-fee-protection net (02 §4.7):
/// <c>CalculateSellerPayout(price, commissionAmount, gasEstimate, ratio)</c>.
/// The estimate used is snapshotted onto <c>BlockchainTransaction.GasFee</c>
/// so the COMPLETED-view split (07 §7.5) is reconstructable from stored data
/// without re-reading a possibly-changed setting.
/// </para>
///
/// <para>
/// Money-safety gate (03 §2.4): a transaction that is on emergency hold or
/// has an active dispute is skipped — its payout is deferred to the
/// admin-resolution path (WP5) / hold release. Idempotency is keyed on the
/// existence of a SELLER_PAYOUT row, so a re-tick before the transaction
/// leaves ITEM_DELIVERED never double-pays the seller.
/// </para>
///
/// <para>
/// <b>Settlement gate (02 §4.5.1) — T126 validation finding F1.</b> Reaching
/// ITEM_DELIVERED is not enough; <see cref="Transaction.PayoutEligibleAt"/>
/// must exist and be in the past. Steam keeps a protected trade reversible for
/// 7 days and either side can reverse it without Steam Support, so paying the
/// seller on delivery alone would leave the buyer without item and without
/// money — the exact fraud path 02 §4.5.1 exists to close.
/// </para>
/// <para>
/// The check is deliberately fail-closed on NULL. It was inert before T126 for
/// a different reason: no production code could fire <c>DeliverItem</c> at all,
/// so ITEM_DELIVERED was unreachable. T126's confirm-receipt endpoint supplied
/// that missing reachability, which is what turned the absent gate into a live
/// exposure and why the gate landed here rather than waiting for T129.
/// </para>
/// <para>
/// <b>T129 completed the gate.</b> An elapsed window is a necessary condition,
/// never a sufficient one: waiting says the reversal period has closed, not
/// that nobody used it. <see cref="Settlement.SettlementVerificationJob"/>
/// answers that second question by re-reading the buyer's inventory and stamps
/// <c>SettlementVerifiedAt</c>; this job now requires that stamp and the absence
/// of <c>DeliveryReversedAt</c>, which is the same pair the COMPLETED guard
/// reads (<c>TransactionStateMachine.HasSettlementClearance</c>). Without the
/// pair here, a reversed transaction would still have its payout queued and
/// broadcast — the money would leave before the state machine ever got the
/// chance to refuse the COMPLETED transition.
/// </para>
///
/// <para>
/// <b>The payout waits for its own sweep (owner decision 2026-09-17).</b>
/// <see cref="SweepQueueJob"/> reads the same settlement gate, so before this
/// change both jobs opened in the same minute: the payout left the hot wallet
/// in seconds while the sweep that funds it went through account activation
/// and Energy delegation. The gap was covered by an operating balance the
/// platform had to park in the hot wallet — DEPLOY_RUNBOOK §I sized it at the
/// largest single payout — and when that balance ran short the transfer
/// reverted on chain and retried. Requiring the CONFIRMED sweep row instead
/// removes the float requirement entirely: the hot wallet only ever pays out
/// money it has already received for that transaction, and what accumulates
/// there is commission. The cost is the sweep's own latency (a few minutes on
/// top of an eight-day window) and a sweep that fails permanently now holds
/// its payout — which the existing sweep-failure alert already surfaces.
/// </para>
///
/// <para>
/// <b>A non-positive net is deferred, not dropped (owner decision 2026-10-01,
/// PayoutStallsOnNonPositiveNet).</b> Since #327 the split takes the runtime
/// estimate, and on mainnet a payout the hot wallet's Energy no longer covers
/// burns 6.43–13.03 TRX (~2.2–4.4 USDT). Below that price the net is zero or
/// negative and nothing may be sent. The job used to log and return without a
/// trace: the same rows were re-priced every minute and, once 20 of them held
/// the oldest-first window, no newer payout was ever queued. Now the row is
/// stamped with a retry time on a backoff (1 h → 4 h → 12 h → every 24 h) and
/// leaves the candidate set until then; the batch is ordered by the deferral
/// count so fresh payouts are always taken first; the third deferral raises
/// <see cref="SellerPayoutDeferredEvent"/> once. Energy regenerates over a
/// day, so the common case — a burst that exhausted the payout share —
/// resolves on a later retry with no manual step.
/// </para>
///
/// <para>
/// Concurrency hardening (WP1 F1 — S2 money-safety). The <c>AnyAsync</c>
/// idempotency check is not atomic with the subsequent insert, so two
/// overlapping ticks could both pass it and queue two PENDING payouts →
/// double-pay. Two layers close the race:
/// <list type="number">
///   <item><see cref="DisableConcurrentExecutionAttribute"/> on
///         <see cref="Execute"/> serialises ticks via a Hangfire distributed
///         lock (single- and multi-instance).</item>
///   <item>The filtered unique index
///         <c>UQ_BlockchainTransactions_SellerPayout_TransactionId</c>
///         (<c>(TransactionId) WHERE Type = 'SELLER_PAYOUT'</c>) is the
///         database-level backstop: a second insert that slips past the lock
///         is rejected, caught here, and treated as an idempotent no-op.</item>
/// </list>
/// </para>
/// </summary>
public sealed class SellerPayoutQueueJob
{
    public const string RecurringJobId = "seller-payout-queue";

    /// <summary>Cron — every minute. Mirrors <c>OutgoingTransferDispatchJob.Cron</c>.</summary>
    public const string Cron = "* * * * *";

    public const int BatchSize = 20;

    /// <summary>
    /// Distributed-lock acquisition timeout for
    /// <see cref="DisableConcurrentExecutionAttribute"/>. Kept shorter than the
    /// 1-minute cron cadence so a contending tick that cannot acquire the lock
    /// abandons before the next tick fires — overlapping waiters never pile up.
    /// </summary>
    public const int ConcurrencyLockTimeoutSeconds = 50;

    /// <summary>
    /// Wait before retrying a payout whose net came out non-positive, indexed
    /// by the deferral count it has just reached (1st → 1 h, 2nd → 4 h, 3rd →
    /// 12 h); the fourth and every later deferral waits the last entry, a day.
    /// Hours, not the minutes the transfer retries use: what has to change is
    /// the hot wallet's Energy, which regenerates over 24 h.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> DeferralBackoff =
    [
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(24),
    ];

    /// <summary>The deferral that alerts the admins — once, as 03 §2.4a Senaryo B escalates after three attempts.</summary>
    public const int EscalateAtDeferral = 3;

    private readonly AppDbContext _db;
    private readonly IRefundDecisionService _refundDecisionService;
    private readonly IChargedGasFeeResolver _chargedGasFee;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<SellerPayoutQueueJob> _logger;

    public SellerPayoutQueueJob(
        AppDbContext db,
        IRefundDecisionService refundDecisionService,
        IChargedGasFeeResolver chargedGasFee,
        IOutboxService outbox,
        TimeProvider clock,
        ILogger<SellerPayoutQueueJob> logger)
    {
        _db = db;
        _refundDecisionService = refundDecisionService;
        _chargedGasFee = chargedGasFee;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        // Soft-delete query filter excludes IsDeleted rows. Skip held /
        // disputed transactions, transactions whose settlement window has not
        // elapsed (02 §4.5.1), those whose sweep has not landed in the hot
        // wallet yet (owner decision 2026-09-17), any that already have a
        // payout row queued, and any whose non-positive net is waiting out its
        // retry time. Never-deferred rows come first: a deferred row is likely
        // to defer again, and ordering by delivery alone let them hold the
        // window (PayoutStallsOnNonPositiveNet).
        var candidateIds = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.Status == TransactionStatus.ITEM_DELIVERED
                && !t.IsOnHold
                && !t.HasActiveDispute
                && t.BlockchainTransactions.Any(
                    b => b.Type == BlockchainTransactionType.SWEEP
                        && b.Status == BlockchainTransactionStatus.CONFIRMED)
                && t.PayoutEligibleAt != null
                && t.PayoutEligibleAt <= nowUtc
                && t.SettlementVerifiedAt != null
                && t.DeliveryReversedAt == null
                && !t.BlockchainTransactions.Any(
                    b => b.Type == BlockchainTransactionType.SELLER_PAYOUT)
                && (t.PayoutDeferredUntil == null || t.PayoutDeferredUntil <= nowUtc))
            .OrderBy(t => t.PayoutDeferralCount)
            .ThenBy(t => t.ItemDeliveredAt)
            .Take(BatchSize)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0) return;

        _logger.LogInformation(
            "SellerPayoutQueueJob picked up {Count} delivered transactions awaiting payout", candidateIds.Count);

        foreach (var id in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await QueuePayoutAsync(id, cancellationToken);
        }
    }

    private async Task QueuePayoutAsync(Guid id, CancellationToken cancellationToken)
    {
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        // Re-validate inside the loop (09 §13.3): a concurrent admin hold,
        // dispute, or completion must not be overwritten by a stale tick. The
        // settlement window is re-read here too — an operator may push
        // PayoutEligibleAt out while this batch is mid-flight, and the batch was
        // selected before that write.
        if (transaction is null
            || transaction.Status != TransactionStatus.ITEM_DELIVERED
            || transaction.IsOnHold
            || transaction.HasActiveDispute
            || transaction.SettlementVerifiedAt is null
            || transaction.DeliveryReversedAt is not null
            || transaction.PayoutEligibleAt is not { } eligibleAt
            || eligibleAt > _clock.GetUtcNow().UtcDateTime
            || transaction.PayoutDeferredUntil > _clock.GetUtcNow().UtcDateTime)
        {
            return;
        }

        // Funding gate, re-read for the same reason the others are: the batch
        // was selected before this row was loaded, and a sweep can only move
        // forward (PENDING → CONFIRMED), never backwards, so reading it again
        // here can only be more conservative.
        var sweepConfirmed = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .AnyAsync(
                b => b.TransactionId == transaction.Id
                    && b.Type == BlockchainTransactionType.SWEEP
                    && b.Status == BlockchainTransactionStatus.CONFIRMED,
                cancellationToken);
        if (!sweepConfirmed) return;

        // Idempotency — never queue a second payout for the same transaction.
        var alreadyQueued = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .AnyAsync(
                b => b.TransactionId == transaction.Id
                    && b.Type == BlockchainTransactionType.SELLER_PAYOUT,
                cancellationToken);
        if (alreadyQueued) return;

        if (string.IsNullOrWhiteSpace(transaction.SellerPayoutAddress))
        {
            _logger.LogError(
                "SellerPayout: transaction {TransactionId} has no SellerPayoutAddress — cannot queue payout.",
                transaction.Id);
            return;
        }

        // Runtime pre-send estimate; the static payout setting only as fallback
        // (Prova-GasFeeChargedIsFixedGuess — owner decision 2026-09-02).
        var resolvedGasFee = await _chargedGasFee.ResolvePayoutFeeAsync(
            transaction.SellerPayoutAddress,
            transaction.Price,
            transaction.StablecoinType,
            cancellationToken);
        var gasEstimate = resolvedGasFee.FeeUsdt;

        // Gas-fee-protection split (02 §4.7) — ResolveSellerPayoutAsync reads
        // the live gas_fee_protection_ratio internally.
        var payout = await _refundDecisionService.ResolveSellerPayoutAsync(
            transaction.Price, transaction.CommissionAmount, gasEstimate, cancellationToken);

        if (payout <= 0m)
        {
            // The gas estimate consumed the whole price: never broadcast a
            // non-positive transfer. Defer with a retry time instead of
            // returning silently (02 §4.7).
            await DeferAsync(transaction, payout, gasEstimate, cancellationToken);
            return;
        }

        var payoutRow = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            PaymentAddressId = null,           // CK_..._Type_Outbound: NULL for SELLER_PAYOUT.
            Type = BlockchainTransactionType.SELLER_PAYOUT,
            TxHash = null,
            FromAddress = string.Empty,        // Stays empty: the sidecar signs with the hot wallet; nothing writes it back.
            ToAddress = transaction.SellerPayoutAddress,
            Amount = payout,
            Token = transaction.StablecoinType,
            ActualTokenAddress = null,         // CK_..._Type_Outbound: NULL for SELLER_PAYOUT.
            GasFee = gasEstimate,              // Snapshot the split input (07 §7.5 reconstruction).
            Status = BlockchainTransactionStatus.PENDING,
            BlockNumber = null,
            ConfirmationCount = 0,
            RetryCount = 0,
            NextAttemptAt = null,              // Eligible for dispatch immediately.
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };

        _db.Set<BlockchainTransaction>().Add(payoutRow);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Money-safety backstop (WP1 F1). A concurrent tick that slipped
            // past the AnyAsync check inserted the SELLER_PAYOUT row first; the
            // filtered unique index rejected this one. Detach the rejected row
            // so the shared job-scope DbContext stays clean for the remaining
            // candidates, then confirm a SELLER_PAYOUT row now exists before
            // swallowing — any unrelated failure re-throws unchanged.
            _db.Entry(payoutRow).State = EntityState.Detached;

            var nowQueued = await _db.Set<BlockchainTransaction>()
                .AsNoTracking()
                .AnyAsync(
                    b => b.TransactionId == transaction.Id
                        && b.Type == BlockchainTransactionType.SELLER_PAYOUT,
                    cancellationToken);
            if (!nowQueued) throw;

            _logger.LogWarning(
                "SellerPayout: concurrent insert race for transaction {TransactionId} — payout already queued by another tick; skipping (idempotent).",
                transaction.Id);
            return;
        }

        _logger.LogInformation(
            "SellerPayout queued — transaction {TransactionId} payout row {RowId} amount {Amount} {Token} (gasEstimate {Gas})",
            transaction.Id, payoutRow.Id, payout, transaction.StablecoinType, gasEstimate);
    }

    private async Task DeferAsync(
        Transaction transaction,
        decimal payout,
        decimal gasEstimate,
        CancellationToken cancellationToken)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var count = transaction.PayoutDeferralCount + 1;
        var nextAttemptAt = nowUtc + DeferralBackoff[Math.Min(count, DeferralBackoff.Count) - 1];

        transaction.PayoutDeferralCount = count;
        transaction.PayoutDeferredUntil = nextAttemptAt;

        // The count only grows, so it equals the threshold on exactly one
        // deferral: the admins hear about a stuck payout once, not daily. The
        // outbox row commits with the stamp below, or not at all.
        var escalated = count == EscalateAtDeferral;
        if (escalated)
        {
            await _outbox.PublishAsync(
                new SellerPayoutDeferredEvent(
                    EventId: Guid.NewGuid(),
                    TransactionId: transaction.Id,
                    SellerId: transaction.SellerId,
                    Price: transaction.Price,
                    CommissionAmount: transaction.CommissionAmount,
                    GasFeeEstimate: gasEstimate,
                    ComputedPayout: payout,
                    DeferralCount: count,
                    NextAttemptAt: nextAttemptAt,
                    OccurredAt: nowUtc),
                cancellationToken);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Another writer (a hold, a dispute) moved the row between the load
            // and this save; RowVersion refuses the stamp instead of writing
            // over it. Drop the stamp and any alert with it — clearing the
            // tracker keeps them out of the next candidate's save — and let the
            // next tick decide against the fresh row.
            _db.ChangeTracker.Clear();
            _logger.LogWarning(
                ex,
                "SellerPayout: transaction {TransactionId} changed while its non-positive payout was being deferred — nothing written, retrying next tick.",
                transaction.Id);
            return;
        }

        _logger.LogWarning(
            "SellerPayout deferred — transaction {TransactionId} computed payout {Payout} (price={Price}, commission={Commission}, gasEstimate={Gas}) is non-positive; deferral {Count}, next attempt {NextAttemptAt:o}{Escalation}.",
            transaction.Id, payout, transaction.Price, transaction.CommissionAmount, gasEstimate,
            count, nextAttemptAt, escalated ? " — admins alerted" : string.Empty);
    }

    // Hangfire serializes Expression<Action<T>>; expose a sync wrapper.
    // [DisableConcurrentExecution] serialises overlapping ticks via a Hangfire
    // distributed lock so two producers cannot queue duplicate payouts (WP1 F1).
    [DisableConcurrentExecution(ConcurrencyLockTimeoutSeconds)]
    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
