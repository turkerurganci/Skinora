using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// WP3 producer leg — per-minute Hangfire job that queues the deposit → hot
/// wallet SWEEP for a transaction once it has settled toward the seller
/// (PRE_F6_PLAN WP3, 05 §3.3 "sweep mekanizması"). Creates the missing
/// PENDING <c>SWEEP</c> <see cref="BlockchainTransaction"/> row; the existing
/// <see cref="OutgoingTransferDispatchJob"/> broadcasts it through the
/// sidecar's <c>/api/transfer/sweep</c> endpoint and
/// <see cref="OutgoingTransferConfirmationJob"/> drives it to CONFIRMED, after
/// which the daily reconciliation (T76) credits it as the hot wallet's inflow.
///
/// <para>
/// <b>Trigger — deferred past the buyer-refund window (owner decision
/// 2026-06-15).</b> 05 §3.3 names <c>PaymentReceivedEvent</c> as the sweep
/// trigger, but the buyer-refund family draws <i>from the deposit address</i>
/// (WP2) and the dominant refund trigger (delivery timeout) fires <i>after</i>
/// payment confirmation. Sweeping eagerly at PAYMENT_RECEIVED would empty the
/// deposit and break the common "cancelled after payment" refund. So this job
/// gates on ITEM_DELIVERED — the point at which the buyer has the item and the
/// transaction settles toward the seller (05 §3.3 line 323: a refund needed
/// before sweep stays deposit-sourced). The hot wallet is an operational pool
/// (05 §3.3 line 307), so the SELLER_PAYOUT (WP1) and this sweep need no strict
/// ordering — both are produced under the same ITEM_DELIVERED gate.
/// </para>
///
/// <para>
/// <b>T129 — the settlement gate applies here for the same reason the deferral
/// does.</b> This job's whole rationale is that the deposit must stay where a
/// refund can draw from it until the transaction has settled toward the seller
/// — and until the end-of-window re-check runs, it has not. A trade reversed on
/// day seven produces exactly the refund this deferral exists to protect, so
/// sweeping at ITEM_DELIVERED alone would empty the deposit shortly before the
/// one refund path most likely to need it. The gate is therefore the same pair
/// the payout job and the COMPLETED guard read: <c>SettlementVerifiedAt</c> set
/// and <c>DeliveryReversedAt</c> null.
/// </para>
///
/// <para>
/// Money-safety gate (mirrors <see cref="SellerPayoutQueueJob"/>): a
/// transaction on emergency hold or with an active dispute is skipped so its
/// deposit stays available for a hold-release / buyer-favour refund. The sweep
/// amount is the full escrowed total (<c>Transaction.TotalAmount</c> = price +
/// commission) — any over-payment is drained separately by the deposit's
/// EXCESS_REFUND, and no gas is deducted (the central sweeper account funds
/// energy via delegation, T74 / 05 §3.3 lines 332-335), so the row carries
/// <c>GasFee = null</c>.
/// </para>
///
/// <para>
/// Concurrency hardening (WP1 F1 money-safety pattern). The <c>AnyAsync</c>
/// idempotency check is not atomic with the insert, so two overlapping ticks
/// could both pass it and queue two PENDING sweeps → double-sweep of the same
/// deposit. Two layers close the race:
/// <list type="number">
///   <item><see cref="DisableConcurrentExecutionAttribute"/> on
///         <see cref="Execute"/> serialises ticks via a Hangfire distributed
///         lock (single- and multi-instance).</item>
///   <item>The filtered unique index
///         <c>UQ_BlockchainTransactions_Sweep_TransactionId</c>
///         (<c>(TransactionId) WHERE Type = 'SWEEP'</c>) is the database-level
///         backstop: a second insert that slips past the lock is rejected,
///         caught here, and treated as an idempotent no-op.</item>
/// </list>
/// </para>
/// </summary>
public sealed class SweepQueueJob
{
    public const string RecurringJobId = "sweep-queue";

    /// <summary>Cron — every minute. Mirrors <c>SellerPayoutQueueJob.Cron</c>.</summary>
    public const string Cron = "* * * * *";

    public const int BatchSize = 20;

    /// <summary>
    /// Distributed-lock acquisition timeout for
    /// <see cref="DisableConcurrentExecutionAttribute"/>. Shorter than the
    /// 1-minute cron cadence so a contending tick that cannot acquire the lock
    /// abandons before the next tick fires (WP1 F1 rationale).
    /// </summary>
    public const int ConcurrencyLockTimeoutSeconds = 50;

    /// <summary>
    /// Hot wallet Tron address SystemSetting key. Canonical definition is
    /// <c>Skinora.API.Services.Reconciliation.ReconciliationService.HotWalletAddressKey</c>;
    /// duplicated here as a literal because that reconciliation service lives in
    /// the API composition root, which Skinora.Transactions cannot reference
    /// (mirrors GasFeeSettingsProvider's own key constants). The seed default is
    /// the "NONE" sentinel — treated as unconfigured until production deploy
    /// sets the real address (06 §3.17 + T76).
    /// </summary>
    public const string HotWalletAddressKey = "reconciliation.hot_wallet_address";

    private const string UnconfiguredSentinel = "NONE";

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<SweepQueueJob> _logger;

    public SweepQueueJob(
        AppDbContext db,
        TimeProvider clock,
        ILogger<SweepQueueJob> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // Resolve the sweep destination once per tick. Until the operator
        // configures it (NONE sentinel), there is nowhere to sweep to — skip
        // the whole run rather than queue rows with a bogus ToAddress (mirrors
        // ReconciliationService / HotWalletService NONE handling).
        var hotWallet = await ResolveHotWalletAddressAsync(cancellationToken);
        if (hotWallet is null)
        {
            _logger.LogWarning(
                "SweepQueueJob skipped: {Key} is unconfigured (NONE) — cannot queue sweeps.",
                HotWalletAddressKey);
            return;
        }

        // Soft-delete query filter excludes IsDeleted rows. Defer past the
        // buyer-refund window (ITEM_DELIVERED), skip held / disputed
        // transactions, and any that already have a sweep row queued.
        var candidateIds = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.Status == TransactionStatus.ITEM_DELIVERED
                && !t.IsOnHold
                && !t.HasActiveDispute
                && t.SettlementVerifiedAt != null
                && t.DeliveryReversedAt == null
                && !t.BlockchainTransactions.Any(
                    b => b.Type == BlockchainTransactionType.SWEEP))
            .OrderBy(t => t.ItemDeliveredAt)
            .Take(BatchSize)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0) return;

        _logger.LogInformation(
            "SweepQueueJob picked up {Count} delivered transactions awaiting sweep", candidateIds.Count);

        foreach (var id in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await QueueSweepAsync(id, hotWallet, cancellationToken);
        }
    }

    private async Task QueueSweepAsync(Guid id, string hotWallet, CancellationToken cancellationToken)
    {
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        // Re-validate inside the loop (09 §13.3): a concurrent admin hold,
        // dispute, or completion must not be overwritten by a stale tick.
        if (transaction is null
            || transaction.Status != TransactionStatus.ITEM_DELIVERED
            || transaction.IsOnHold
            || transaction.HasActiveDispute
            || transaction.SettlementVerifiedAt is null
            || transaction.DeliveryReversedAt is not null)
        {
            return;
        }

        // Idempotency layer 1 — never queue a second sweep for the same
        // transaction. A re-tick before the row is visible finds it and no-ops.
        var alreadyQueued = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .AnyAsync(
                b => b.TransactionId == transaction.Id
                    && b.Type == BlockchainTransactionType.SWEEP,
                cancellationToken);
        if (alreadyQueued) return;

        // The deposit address is the SWEEP source (deposit → hot wallet). It is
        // the transaction's 1:1 PaymentAddress; the dispatcher re-derives the HD
        // index from it at broadcast time. A delivered transaction always has a
        // confirmed deposit, but guard defensively.
        var deposit = await _db.Set<PaymentAddress>()
            .AsNoTracking()
            .Where(p => p.TransactionId == transaction.Id)
            .Select(p => new { p.Id, p.Address })
            .FirstOrDefaultAsync(cancellationToken);
        if (deposit is null)
        {
            _logger.LogError(
                "Sweep: transaction {TransactionId} has no deposit PaymentAddress — cannot queue sweep.",
                transaction.Id);
            return;
        }

        var sweepRow = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            PaymentAddressId = deposit.Id,     // CK_..._Type_Sweep: NOT NULL (deposit-anchored source).
            Type = BlockchainTransactionType.SWEEP,
            TxHash = null,
            FromAddress = deposit.Address,      // Source deposit; dispatcher re-resolves the same address.
            ToAddress = hotWallet,              // Sweep destination (reconciliation hot-wallet inflow key).
            Amount = transaction.TotalAmount,   // Full escrowed total (price + commission); excess drained separately.
            Token = transaction.StablecoinType,
            ActualTokenAddress = null,          // CK_..._Type_Sweep: NULL (canonical stablecoin, no wrong-token).
            GasFee = null,                      // Sweeper account funds energy via delegation (05 §3.3); not deducted.
            Status = BlockchainTransactionStatus.PENDING,
            BlockNumber = null,
            ConfirmationCount = 0,
            RetryCount = 0,
            NextAttemptAt = null,               // Eligible for dispatch immediately.
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };

        _db.Set<BlockchainTransaction>().Add(sweepRow);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Money-safety backstop (WP1 F1). A concurrent tick that slipped
            // past the AnyAsync check inserted the SWEEP row first; the filtered
            // unique index rejected this one. Detach the rejected row so the
            // shared job-scope DbContext stays clean for the remaining
            // candidates, then confirm a SWEEP row now exists before swallowing
            // — any unrelated failure re-throws unchanged.
            _db.Entry(sweepRow).State = EntityState.Detached;

            var nowQueued = await _db.Set<BlockchainTransaction>()
                .AsNoTracking()
                .AnyAsync(
                    b => b.TransactionId == transaction.Id
                        && b.Type == BlockchainTransactionType.SWEEP,
                    cancellationToken);
            if (!nowQueued) throw;

            _logger.LogWarning(
                "Sweep: concurrent insert race for transaction {TransactionId} — sweep already queued by another tick; skipping (idempotent).",
                transaction.Id);
            return;
        }

        _logger.LogInformation(
            "Sweep queued — transaction {TransactionId} sweep row {RowId} amount {Amount} {Token} from deposit {Deposit} → hot wallet",
            transaction.Id, sweepRow.Id, transaction.TotalAmount, transaction.StablecoinType, deposit.Address);
    }

    private async Task<string?> ResolveHotWalletAddressAsync(CancellationToken cancellationToken)
    {
        var row = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == HotWalletAddressKey)
            .Select(s => new { s.Value, s.IsConfigured })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null || !row.IsConfigured || string.IsNullOrWhiteSpace(row.Value)) return null;
        var value = row.Value.Trim();
        return string.Equals(value, UnconfiguredSentinel, StringComparison.Ordinal) ? null : value;
    }

    // Hangfire serializes Expression<Action<T>>; expose a sync wrapper.
    // [DisableConcurrentExecution] serialises overlapping ticks via a Hangfire
    // distributed lock so two producers cannot queue duplicate sweeps (WP1 F1).
    [DisableConcurrentExecution(ConcurrencyLockTimeoutSeconds)]
    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
