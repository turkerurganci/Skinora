using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
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

    private readonly AppDbContext _db;
    private readonly IRefundDecisionService _refundDecisionService;
    private readonly IGasFeeSettingsProvider _gasFeeSettings;
    private readonly TimeProvider _clock;
    private readonly ILogger<SellerPayoutQueueJob> _logger;

    public SellerPayoutQueueJob(
        AppDbContext db,
        IRefundDecisionService refundDecisionService,
        IGasFeeSettingsProvider gasFeeSettings,
        TimeProvider clock,
        ILogger<SellerPayoutQueueJob> logger)
    {
        _db = db;
        _refundDecisionService = refundDecisionService;
        _gasFeeSettings = gasFeeSettings;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // Soft-delete query filter excludes IsDeleted rows. Skip held /
        // disputed transactions and any that already have a payout row queued.
        var candidateIds = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.Status == TransactionStatus.ITEM_DELIVERED
                && !t.IsOnHold
                && !t.HasActiveDispute
                && !t.BlockchainTransactions.Any(
                    b => b.Type == BlockchainTransactionType.SELLER_PAYOUT))
            .OrderBy(t => t.ItemDeliveredAt)
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
        // dispute, or completion must not be overwritten by a stale tick.
        if (transaction is null
            || transaction.Status != TransactionStatus.ITEM_DELIVERED
            || transaction.IsOnHold
            || transaction.HasActiveDispute)
        {
            return;
        }

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

        var gasSettings = await _gasFeeSettings.GetAsync(cancellationToken);
        var gasEstimate = gasSettings.PayoutGasFeeEstimateUsdt;

        // Gas-fee-protection split (02 §4.7) — ResolveSellerPayoutAsync reads
        // the live gas_fee_protection_ratio internally.
        var payout = await _refundDecisionService.ResolveSellerPayoutAsync(
            transaction.Price, transaction.CommissionAmount, gasEstimate, cancellationToken);

        if (payout <= 0m)
        {
            // Pathological: gas estimate consumed the whole price. Do not
            // broadcast a non-positive transfer — leave the transaction in
            // ITEM_DELIVERED for operator review (03 §2.4a Senaryo B).
            _logger.LogError(
                "SellerPayout: computed payout {Payout} for transaction {TransactionId} (price={Price}, commission={Commission}, gasEstimate={Gas}) is non-positive — skipping.",
                payout, transaction.Id, transaction.Price, transaction.CommissionAmount, gasEstimate);
            return;
        }

        var payoutRow = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            PaymentAddressId = null,           // CK_..._Type_Outbound: NULL for SELLER_PAYOUT.
            Type = BlockchainTransactionType.SELLER_PAYOUT,
            TxHash = null,
            FromAddress = string.Empty,        // Hot-wallet address set at broadcast time.
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

    // Hangfire serializes Expression<Action<T>>; expose a sync wrapper.
    // [DisableConcurrentExecution] serialises overlapping ticks via a Hangfire
    // distributed lock so two producers cannot queue duplicate payouts (WP1 F1).
    [DisableConcurrentExecution(ConcurrencyLockTimeoutSeconds)]
    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
