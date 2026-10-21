using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Application.Wallets;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Blockchain;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
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
        // T160c — collection of withheld refund fees; confirms like a sweep.
        BlockchainTransactionType.REFUND_FEE_SWEEP,
    ];

    private readonly AppDbContext _db;
    private readonly IBlockchainServiceRegistry _services;
    private readonly IBlockchainNetworkHealth _health;
    private readonly IOutboxService _outbox;
    private readonly IStablecoinContractRegistry _contracts;
    private readonly IPlatformWalletAddressProvider _walletAddresses;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutgoingTransferConfirmationJob> _logger;

    public OutgoingTransferConfirmationJob(
        AppDbContext db,
        IBlockchainServiceRegistry services,
        IBlockchainNetworkHealth health,
        IOutboxService outbox,
        IStablecoinContractRegistry contracts,
        IPlatformWalletAddressProvider walletAddresses,
        TimeProvider clock,
        ILogger<OutgoingTransferConfirmationJob> logger)
    {
        _db = db;
        _services = services;
        _health = health;
        _outbox = outbox;
        _contracts = contracts;
        _walletAddresses = walletAddresses;
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
        // network's confirmations.
        //
        // T152 — rotation inside a network: a DETECTED row's NextAttemptAt is
        // stamped each time it is asked and still not final, and the batch is
        // taken least-recently-asked first (never-asked rows, NULL, lead). More
        // than BatchSize long-pending rows (Ethereum waits ~15 min for
        // finalized) therefore take turns instead of the oldest 30 holding the
        // batch forever.
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
                .OrderBy(b => b.NextAttemptAt)
                .ThenBy(b => b.CreatedAt)
                .Take(BatchSize)
                .Select(b => b.Id)
                .ToListAsync(cancellationToken);
            perNetwork.Add(ids.Select(id => (network, id)).ToList());
        }

        var candidates = NetworkWorkQueue.Interleave(perNetwork);
        if (candidates.Count == 0) return;

        // T152 (T150 doğrulaması) — the breaker opens only on Unavailable, i.e.
        // a transport failure or a 5xx (HttpBlockchainTransferClient); a 4xx
        // about one row (Rejected) leaves the network's other rows in the run.
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
        var status = await _services.Transfers(row.Network)
            .GetStatusAsync(row.TxHash, ExpectationFor(row, policy), cancellationToken);
        switch (status.Outcome)
        {
            case TransferStatusOutcome.Confirmed:
                AdoptReportedHash(row, status);
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
                AdoptReportedHash(row, status);
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

            case TransferStatusOutcome.Unverified:
                // T158 (g) — final on chain but the service cannot tie it to
                // this row (its journal does not hold the hash and the
                // Transfer log does not match). Fail closed: never CONFIRMED
                // (the money may not have reached the recipient), never
                // FAILED (it may have — a FAILED row invites a re-send).
                // The row stays DETECTED, visible with its reason; the
                // service's ethereum-transfer-status-unverified alarm fires.
                row.ErrorMessage = TruncateError(
                    $"TRANSFER_UNVERIFIED: tx {row.TxHash} is final but its Transfer log does not match this row — not confirmed; investigate (DEPLOY_RUNBOOK §J).");
                _logger.LogError(
                    "Outbound transfer UNVERIFIED — row {Id} ({Type}) on {Network} tx {TxHash}: final on chain, but the service could not match its Transfer log to the row; left DETECTED (not confirmed, not failed)",
                    row.Id, row.Type, row.Network, row.TxHash);
                await StampAskedAsync(row, cancellationToken);
                return false;

            case TransferStatusOutcome.Unavailable:
                _logger.LogWarning(
                    "Outbound transfer status unavailable — row {Id} tx {TxHash}: {Message}",
                    row.Id, row.TxHash, status.ErrorMessage);
                return true;

            case TransferStatusOutcome.Rejected:
                _logger.LogWarning(
                    "Outbound transfer status refused for this row — row {Id} ({Type}) tx {TxHash}: {Message}; the network's other rows continue",
                    row.Id, row.Type, row.TxHash, status.ErrorMessage);
                await StampAskedAsync(row, cancellationToken);
                return false;

            case TransferStatusOutcome.Pending:
            default:
                // Not final yet — keep DETECTED; only the rotation stamp moves.
                await StampAskedAsync(row, cancellationToken);
                return false;
        }
    }

    /// <summary>
    /// T158 (g) — what this row expects on chain, sent with the status read on
    /// a network whose service verifies a hash its journal does not hold
    /// (Ethereum). The contract is the row's token's (a wrong-token refund
    /// moves the token that arrived); the sender is the row's
    /// <c>FromAddress</c> (deposit, or the hot wallet for a post-sweep refund)
    /// and, for a payout — which never records one — the network's hot wallet.
    /// Null when something is unknown: the service then answers UNKNOWN for an
    /// unjournalled hash (fail closed), a journalled one is unaffected.
    /// </summary>
    private TransferStatusExpectation? ExpectationFor(BlockchainTransaction row, BlockchainNetworkPolicy policy)
    {
        if (!policy.VerifiesUnjournalledTransferStatus) return null;
        // T157 düzeltme turu — the same rule the dispatcher signs with and the
        // closure tool verifies with (a wrong-token refund expects the
        // allowlisted token that arrived; an unresolvable one expects nothing).
        return OutboundTransferExpectation.For(row, _contracts, _walletAddresses);
    }

    /// <summary>T152 — send the row to the back of its network's rotation.</summary>
    private async Task StampAskedAsync(BlockchainTransaction row, CancellationToken cancellationToken)
    {
        row.NextAttemptAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// T152 — an Ethereum fee bump re-signs the same nonce, so the transaction
    /// in the block can have another hash than the one stored at broadcast.
    /// The final row carries the hash that is on chain.
    /// </summary>
    private void AdoptReportedHash(BlockchainTransaction row, TransferStatusResult status)
    {
        if (string.IsNullOrWhiteSpace(status.TxHash)) return;
        var reported = BlockchainAddress.Canonicalize(status.TxHash);
        if (string.Equals(reported, row.TxHash, StringComparison.Ordinal)) return;
        _logger.LogWarning(
            "Outbound transfer row {Id} ({Type}) landed as {Reported} (stored {Stored}) — a same-nonce replacement; the row now carries the on-chain hash",
            row.Id, row.Type, reported, row.TxHash);
        row.TxHash = reported;
    }

    private static string? TruncateError(string? message)
    {
        if (string.IsNullOrEmpty(message)) return null;
        return message.Length > 500 ? message[..500] : message;
    }

    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
