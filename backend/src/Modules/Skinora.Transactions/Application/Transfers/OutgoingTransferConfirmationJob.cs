using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Blockchain;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// Per-minute Hangfire job that pulls finality data for previously broadcast
/// outbound transfers (T73, 05 §3.3). Walks
/// <c>BlockchainTransaction.Status=DETECTED</c> rows with a non-null
/// <c>TxHash</c>, asks the sidecar for the solidity-node view, and either
/// flips the row to CONFIRMED (≥20 blocks) or FAILED (contract reverted).
///
/// <para>
/// The 20-block threshold is enforced by <c>HttpBlockchainTransferClient</c>
/// (08 §3.4 + 05 §3.3); this job only consumes the discriminated outcome.
/// Inbound rows (BUYER_PAYMENT / WRONG_TOKEN_INCOMING / SPAM_TOKEN_INCOMING)
/// are confirmed via the dedicated webhook handler (T71) — not this loop.
/// </para>
/// </summary>
public sealed class OutgoingTransferConfirmationJob
{
    public const string RecurringJobId = "outgoing-transfer-confirmation";

    public const string Cron = "* * * * *";

    public const int BatchSize = 30;

    private static readonly BlockchainTransactionType[] OutboundTypes =
    [
        BlockchainTransactionType.SELLER_PAYOUT,
        BlockchainTransactionType.BUYER_REFUND,
        BlockchainTransactionType.EXCESS_REFUND,
        BlockchainTransactionType.WRONG_TOKEN_REFUND,
        BlockchainTransactionType.INCORRECT_AMOUNT_REFUND,
        BlockchainTransactionType.LATE_PAYMENT_REFUND,
        // WP3 — a broadcast SWEEP must be driven DETECTED → CONFIRMED here too,
        // otherwise it sits in DETECTED forever and the CONFIRMED-only daily
        // reconciliation (T76) never credits the hot wallet inflow. SWEEP drives
        // no transaction-state transition (only SELLER_PAYOUT emits
        // PayoutCompletedEvent below), so it simply flips to CONFIRMED.
        BlockchainTransactionType.SWEEP,
    ];

    private readonly AppDbContext _db;
    private readonly IBlockchainServiceRegistry _services;
    private readonly IBlockchainNetworkHealth _health;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutgoingTransferConfirmationJob> _logger;

    public OutgoingTransferConfirmationJob(
        AppDbContext db,
        IBlockchainServiceRegistry services,
        IBlockchainNetworkHealth health,
        IOutboxService outbox,
        TimeProvider clock,
        ILogger<OutgoingTransferConfirmationJob> logger)
    {
        _db = db;
        _services = services;
        _health = health;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // T148 düzeltme turu 1 (F1) — a DETECTED row whose network's service is
        // no longer configured cannot be answered (the fail-closed client says
        // Unavailable and the row stays DETECTED). Selecting it would keep it at
        // the head of the oldest-first batch forever and starve every other
        // network's confirmations, so only configured networks are candidates.
        //
        // T150 — and only networks not in a detected outage; each reachable
        // network gets its own oldest-first batch, worked round-robin, and the
        // first "unavailable" answer of a network ends its turn for this run
        // (NetworkWorkQueue). A down service therefore never holds another
        // network's confirmations. (Rotation among one network's own long-
        // pending rows is T152's criterion.)
        var reachable = _services.ReachableNetworks(_health);
        if (reachable.Count == 0) return;

        var perNetwork = new List<IReadOnlyList<(BlockchainNetwork Network, Guid Id)>>();
        foreach (var network in reachable)
        {
            var ids = await _db.Set<BlockchainTransaction>()
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(b => b.Status == BlockchainTransactionStatus.DETECTED
                    && b.TxHash != null
                    && OutboundTypes.Contains(b.Type)
                    && b.Network == network)
                .OrderBy(b => b.CreatedAt)
                .Take(BatchSize)
                .Select(b => b.Id)
                .ToListAsync(cancellationToken);
            perNetwork.Add(ids.Select(id => (network, id)).ToList());
        }

        var candidates = NetworkWorkQueue.Interleave(perNetwork);
        if (candidates.Count == 0) return;

        var breaker = new NetworkRunBreaker();
        foreach (var (network, id) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (breaker.IsTripped(network)) continue;

            var serviceUnavailable = await ConfirmOneAsync(id, cancellationToken);
            if (serviceUnavailable && breaker.Trip(network))
            {
                _logger.LogWarning(
                    "{Network} blockchain service did not answer a status read — its remaining DETECTED rows wait for the next run; other networks continue.",
                    network);
            }
        }
    }

    /// <returns>True when the network's service could not answer (Unavailable).</returns>
    private async Task<bool> ConfirmOneAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _db.Set<BlockchainTransaction>()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (row is null || row.Status != BlockchainTransactionStatus.DETECTED || row.TxHash is null) return false;

        // T148 — the row's own network's service answers, and the network's
        // policy (not a TRON constant) fills in what the answer left out.
        var policy = BlockchainNetworkPolicy.For(row.Network);
        var status = await _services.Transfers(row.Network).GetStatusAsync(row.TxHash, cancellationToken);
        switch (status.Outcome)
        {
            case TransferStatusOutcome.Confirmed:
                row.Status = BlockchainTransactionStatus.CONFIRMED;
                row.BlockNumber = status.BlockNumber;
                row.ConfirmationCount = policy.RecordedConfirmationCount(status.Confirmations);
                row.ConfirmedAt = _clock.GetUtcNow().UtcDateTime;
                row.ErrorMessage = null;
                // Realized cost, recorded for measurement only. The charge in
                // GasFee was fixed when the row was queued and is NOT revised
                // here: collecting a difference afterwards would need a second
                // transfer costing more than the difference. What this enables
                // is the question the estimate itself raises — how close does
                // it land — which was previously assumed rather than measured.
                // TRON's SUN / Energy receipt fields — recorded only for a
                // network that has them (Ethereum's realized fee is wei, T154).
                if (policy.ReportsTronResourceUsage)
                {
                    row.RealizedFeeSun = status.RealizedFeeSun;
                    row.EnergyUsageTotal = status.EnergyUsageTotal;
                    row.OriginEnergyUsage = status.OriginEnergyUsage;
                }
                // T154 — Ethereum's realized fee, wei, exact.
                if (policy.ReportsFeeInWei)
                {
                    row.RealizedFeeWei = status.RealizedFeeWei;
                }

                // WP1 — a confirmed SELLER_PAYOUT means the seller is paid on
                // chain (03 §2.4 step 6). Publish PayoutCompletedEvent in the
                // same unit of work as the CONFIRMED flip so the completion
                // consumer can fire Complete → COMPLETED. Refund rows do not
                // drive a state transition, so they are not announced here.
                if (row.Type == BlockchainTransactionType.SELLER_PAYOUT)
                {
                    await _outbox.PublishAsync(
                        new PayoutCompletedEvent(
                            EventId: Guid.NewGuid(),
                            TransactionId: row.TransactionId,
                            PayoutTxHash: row.TxHash,
                            NetAmount: row.Amount,
                            OccurredAt: row.ConfirmedAt.Value),
                        cancellationToken);
                }

                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Outbound transfer CONFIRMED — row {Id} ({Type}) on {Network} tx {TxHash} @ block {Block}; charged {Charged} USDT, realized {RealizedSun} SUN (energy {Energy}, owner-paid {OwnerEnergy}) / {RealizedWei} wei",
                    row.Id, row.Type, row.Network, row.TxHash, status.BlockNumber,
                    row.GasFee, status.RealizedFeeSun, status.EnergyUsageTotal, status.OriginEnergyUsage,
                    status.RealizedFeeWei);
                return false;

            case TransferStatusOutcome.Failed:
                row.Status = BlockchainTransactionStatus.FAILED;
                row.BlockNumber = status.BlockNumber;
                row.ConfirmationCount = status.Confirmations ?? 0;
                row.ErrorMessage = TruncateError(
                    $"On-chain failure: contractRet={status.ContractRet ?? "(none)"}");
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogError(
                    "Outbound transfer on-chain FAILED — row {Id} ({Type}) tx {TxHash} contractRet={ContractRet}",
                    row.Id, row.Type, row.TxHash, status.ContractRet);
                return false;

            case TransferStatusOutcome.Unavailable:
                _logger.LogWarning(
                    "Outbound transfer status unavailable — row {Id} tx {TxHash}: {Message}",
                    row.Id, row.TxHash, status.ErrorMessage);
                return true;

            case TransferStatusOutcome.Pending:
            default:
                // Solidity node has not caught up — keep DETECTED, no row mutation.
                return false;
        }
    }

    private static string? TruncateError(string? message)
    {
        if (string.IsNullOrEmpty(message)) return null;
        return message.Length > 500 ? message[..500] : message;
    }

    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
