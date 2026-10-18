using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// Per-minute Hangfire job that drains the outbound-transfer queue
/// (T73, 08 §3.3, 05 §3.3). Picks up PENDING <c>BlockchainTransaction</c>
/// rows of outbound type (SELLER_PAYOUT / *_REFUND) and asks the blockchain
/// sidecar to broadcast them.
///
/// <para>
/// Retry strategy: a transient sidecar failure increments
/// <c>RetryCount</c> and stamps <c>NextAttemptAt</c> with the configured
/// backoff. When the policy is exhausted the row is flipped to
/// <c>FAILED</c> and <see cref="TransferDispatchFailedEvent"/> is published
/// for admin alerting.
/// </para>
///
/// <para>
/// State validation (09 §13.3 job-handler pattern): each row is re-read
/// inside the loop so a concurrent admin action (force-cancel, manual
/// retry) cannot be overwritten by a stale dispatcher tick.
/// </para>
///
/// <para>
/// One tick at a time (<see cref="DisableConcurrentExecutionAttribute"/>). A
/// deposit-sourced broadcast now takes several blocks on the sidecar (08 §3.3
/// — every resource step waits for its block), so a batch outlives the
/// one-minute cadence. The re-read above cannot close the overlap: two ticks
/// can both read a row as PENDING before either writes, and both would run the
/// sidecar flow — activation, delegation and transfer — for the same deposit.
/// </para>
/// </summary>
public sealed class OutgoingTransferDispatchJob
{
    public const string RecurringJobId = "outgoing-transfer-dispatch";

    /// <summary>Cron — every minute. Mirrors <c>EnsurePaymentAddressJob.Cron</c>.</summary>
    public const string Cron = "* * * * *";

    /// <summary>
    /// Distributed-lock acquisition timeout for
    /// <see cref="DisableConcurrentExecutionAttribute"/>. Shorter than the
    /// one-minute cadence so a tick that cannot get the lock gives up before
    /// the next one fires (same rationale as <c>SweepQueueJob</c>).
    /// </summary>
    public const int ConcurrencyLockTimeoutSeconds = 50;

    /// <summary>
    /// Maximum rows processed per tick. The 1-minute cadence keeps a 100-row
    /// burst at well under the sidecar's 10 RPS budget (08 §3.1).
    /// </summary>
    public const int BatchSize = 20;

    private static readonly BlockchainTransactionType[] OutboundTypes =
    [
        BlockchainTransactionType.SELLER_PAYOUT,
        BlockchainTransactionType.BUYER_REFUND,
        BlockchainTransactionType.EXCESS_REFUND,
        BlockchainTransactionType.WRONG_TOKEN_REFUND,
        BlockchainTransactionType.INCORRECT_AMOUNT_REFUND,
        BlockchainTransactionType.LATE_PAYMENT_REFUND,
        // WP3 — deposit → hot wallet sweep. Reuses the non-SELLER_PAYOUT
        // source-resolution branch below (deposit index/address from the
        // sibling BUYER_PAYMENT row); BuildRequest routes it to the sidecar's
        // /api/transfer/sweep endpoint (HttpBlockchainTransferClient).
        BlockchainTransactionType.SWEEP,
    ];

    private readonly AppDbContext _db;
    private readonly IBlockchainServiceRegistry _services;
    private readonly IBlockchainNetworkHealth _health;
    private readonly ITransferRetryPolicy _retryPolicy;
    private readonly IOutboxService _outbox;
    private readonly IStablecoinContractRegistry _contracts;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutgoingTransferDispatchJob> _logger;

    public OutgoingTransferDispatchJob(
        AppDbContext db,
        IBlockchainServiceRegistry services,
        IBlockchainNetworkHealth health,
        ITransferRetryPolicy retryPolicy,
        IOutboxService outbox,
        IStablecoinContractRegistry contracts,
        TimeProvider clock,
        ILogger<OutgoingTransferDispatchJob> logger)
    {
        _db = db;
        _services = services;
        _health = health;
        _retryPolicy = retryPolicy;
        _outbox = outbox;
        _contracts = contracts;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        // T148 düzeltme turu 1 (F1) — only rows of a network whose service is
        // configured are candidates. A row on an unconfigured network is parked
        // (never sent, NextAttemptAt stays null), so selecting it would put it
        // back at the head of the oldest-first batch on every run: BatchSize
        // parked rows would starve every other network's payouts, refunds and
        // sweeps. Filtering in the query keeps the batch for rows that can move.
        var configured = _services.ConfiguredNetworks.ToList();
        await LogParkedRowsAsync(configured, cancellationToken);

        // T150 — a configured network in a detected outage is skipped the same
        // way (its rows keep their retry budget), every reachable network gets
        // its own oldest-first batch, and the batches are worked round-robin
        // (NetworkWorkQueue).
        var reachable = _services.ReachableNetworks(_health);
        foreach (var network in configured.Except(reachable))
        {
            _logger.LogWarning(
                "Outbound rows on {Network} wait: its blockchain service is in a detected outage — nothing sent, retry budget kept, other networks unaffected.",
                network);
        }
        if (reachable.Count == 0) return;

        var perNetwork = new List<IReadOnlyList<(BlockchainNetwork Network, Guid Id)>>();
        foreach (var network in reachable)
        {
            var ids = await _db.Set<BlockchainTransaction>()
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(b => b.Status == BlockchainTransactionStatus.PENDING
                    && OutboundTypes.Contains(b.Type)
                    && b.Network == network
                    && (b.NextAttemptAt == null || b.NextAttemptAt <= now))
                .OrderBy(b => b.CreatedAt)
                .Take(BatchSize)
                .Select(b => b.Id)
                .ToListAsync(cancellationToken);
            perNetwork.Add(ids.Select(id => (network, id)).ToList());
        }

        var candidates = NetworkWorkQueue.Interleave(perNetwork);
        if (candidates.Count == 0) return;

        _logger.LogInformation(
            "OutgoingTransferDispatchJob picked up {Count} eligible rows", candidates.Count);

        var breaker = new NetworkRunBreaker();
        foreach (var (network, id) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (breaker.IsTripped(network)) continue;

            var serviceUnreachable = await DispatchOneAsync(id, cancellationToken);
            if (serviceUnreachable && breaker.Trip(network))
            {
                _logger.LogWarning(
                    "{Network} blockchain service is unreachable — its remaining outbound rows wait for the next run (nothing sent, retry budget kept); other networks continue.",
                    network);
            }
        }
    }

    /// <summary>
    /// One error line per run and unconfigured network that still holds
    /// PENDING outbound rows — the visibility the per-row log gave before the
    /// candidate query learned to skip them.
    /// </summary>
    private async Task LogParkedRowsAsync(
        List<BlockchainNetwork> configured,
        CancellationToken cancellationToken)
    {
        var unconfigured = Enum.GetValues<BlockchainNetwork>()
            .Where(n => !configured.Contains(n))
            .ToList();
        if (unconfigured.Count == 0) return;

        var parked = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(b => b.Status == BlockchainTransactionStatus.PENDING
                && OutboundTypes.Contains(b.Type)
                && unconfigured.Contains(b.Network))
            .GroupBy(b => b.Network)
            .Select(g => new { Network = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        foreach (var group in parked)
        {
            _logger.LogError(
                "{Count} outbound row(s) on {Network} are waiting PENDING: that network has no blockchain service configured — nothing sent, other networks unaffected.",
                group.Count, group.Network);
        }
    }

    private async Task<bool> DispatchOneAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _db.Set<BlockchainTransaction>()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (row is null || row.Status != BlockchainTransactionStatus.PENDING) return false;

        // T148 — fail closed per network (05 §3.3a). A row whose network has no
        // service configured is left PENDING untouched: no broadcast, no retry
        // budget spent, no FAILED flip. The candidate query already skips such
        // rows, so they never take another network's batch slots; this
        // re-check is the defensive second layer. The row goes out as soon as
        // its service is configured. (Before T147 every row is TRON.)
        if (!_services.IsConfigured(row.Network))
        {
            _logger.LogError(
                "Outbound row {Id} ({Type}) is on {Network}, which has no blockchain service configured — left PENDING, nothing sent.",
                row.Id, row.Type, row.Network);
            return false;
        }

        // For refund/sweep flows the deposit address is the PaymentAddress
        // that owns the original BUYER_PAYMENT row. Resolve it via the
        // sibling row so we never derive the wrong index.
        int? depositIndex = null;
        string? depositAddress = null;
        if (row.Type != BlockchainTransactionType.SELLER_PAYOUT)
        {
            var sourcePayment = await _db.Set<BlockchainTransaction>()
                .AsNoTracking()
                .Where(b => b.TransactionId == row.TransactionId
                    && (b.Type == BlockchainTransactionType.BUYER_PAYMENT
                        || b.Type == BlockchainTransactionType.WRONG_TOKEN_INCOMING)
                    && b.PaymentAddressId != null)
                .OrderBy(b => b.CreatedAt)
                .Select(b => new { b.PaymentAddressId })
                .FirstOrDefaultAsync(cancellationToken);
            if (sourcePayment is null || sourcePayment.PaymentAddressId is null)
            {
                _logger.LogWarning(
                    "Dispatcher could not resolve deposit address for outbound row {Id} ({Type}, transaction {TransactionId}) — skipping",
                    row.Id, row.Type, row.TransactionId);
                return false;
            }
            var addressRow = await _db.Set<PaymentAddress>()
                .AsNoTracking()
                .Where(p => p.Id == sourcePayment.PaymentAddressId.Value)
                .Select(p => new { p.Address, p.HdWalletIndex })
                .FirstOrDefaultAsync(cancellationToken);
            if (addressRow is null)
            {
                _logger.LogWarning(
                    "Dispatcher could not resolve PaymentAddress row {PaymentAddressId} for outbound {Id} ({Type})",
                    sourcePayment.PaymentAddressId, row.Id, row.Type);
                return false;
            }
            depositIndex = addressRow.HdWalletIndex;
            depositAddress = addressRow.Address;
            row.FromAddress = addressRow.Address;
        }

        // 06 §3.8 stores the EXPECTED stablecoin in Token for the wrong-token
        // family and the contract that actually landed on the deposit address
        // in ActualTokenAddress. The sidecar resolves the symbol we send here
        // to a contract (RefundService.resolveContract), so forwarding
        // row.Token would ask the deposit address to move a token it does not
        // hold — the refund could never broadcast. Translate the stored
        // contract back to its symbol instead; the sidecar's allowlist means
        // it is always USDT or USDC (AmountValidationService §wrong-token).
        var broadcastToken = row.Token;
        if (row.Type == BlockchainTransactionType.WRONG_TOKEN_REFUND)
        {
            var actualToken = _contracts.ResolveByContract(row.Network, row.ActualTokenAddress);
            if (actualToken is null)
            {
                // Unresolvable contract cannot be broadcast under any symbol.
                // Fail terminally rather than send the expected token and let
                // the chain reject it: same admin alert, no wasted attempts.
                await HandleFailureAsync(
                    row,
                    new TransferBroadcastResult(
                        TransferBroadcastStatus.InvalidRequest,
                        null,
                        "WRONG_TOKEN_CONTRACT_UNRESOLVED",
                        $"ActualTokenAddress '{row.ActualTokenAddress}' does not match a configured stablecoin contract."),
                    terminal: true,
                    cancellationToken);
                return false;
            }

            broadcastToken = actualToken.Value;
        }

        var request = new TransferBroadcastRequest(
            BlockchainTransactionId: row.Id,
            Type: row.Type,
            Token: broadcastToken,
            Amount: row.Amount,
            ToAddress: row.ToAddress,
            DepositIndex: depositIndex,
            DepositAddress: depositAddress);

        // Same-network rule (11 §F8 K3): the row's own network's service.
        var result = await _services.Transfers(row.Network).BroadcastAsync(request, cancellationToken);
        switch (result.Status)
        {
            case TransferBroadcastStatus.Success:
                await HandleSuccessAsync(row, result, cancellationToken);
                return false;

            case TransferBroadcastStatus.InvalidRequest:
                // Sidecar refused the payload outright — no further attempts
                // help. Flip to FAILED and alert immediately.
                await HandleFailureAsync(row, result, terminal: true, cancellationToken);
                return false;

            case TransferBroadcastStatus.TransientFailure:
            default:
                await HandleTransientAsync(row, result, cancellationToken);
                return result.ErrorCode == HttpBlockchainTransferClient.TransportFailureCode;
        }
    }

    private async Task HandleSuccessAsync(
        BlockchainTransaction row,
        TransferBroadcastResult result,
        CancellationToken cancellationToken)
    {
        row.TxHash = result.TxHash;
        row.Status = BlockchainTransactionStatus.DETECTED;
        row.NextAttemptAt = null;
        row.ErrorMessage = null;
        // `RetryCount` and `ConfirmationCount` are preserved — confirmation
        // job flips DETECTED → CONFIRMED once the solidity node catches up.

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Outbound transfer broadcast OK — row {Id} ({Type}) → txHash {TxHash}",
            row.Id, row.Type, result.TxHash);
    }

    private async Task HandleTransientAsync(
        BlockchainTransaction row,
        TransferBroadcastResult result,
        CancellationToken cancellationToken)
    {
        var nextRetryCount = row.RetryCount + 1;
        var delay = await _retryPolicy.GetRetryDelayAsync(nextRetryCount - 1, cancellationToken);
        if (delay is null)
        {
            // Policy exhausted — terminal failure.
            await HandleFailureAsync(row, result, terminal: true, cancellationToken);
            return;
        }

        row.RetryCount = nextRetryCount;
        row.NextAttemptAt = _clock.GetUtcNow().UtcDateTime.Add(delay.Value);
        row.ErrorMessage = TrimError(result.ErrorCode, result.ErrorMessage);

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning(
            "Outbound transfer transient failure — row {Id} ({Type}) attempt {Retry}, retrying after {Delay}: {Code} — {Message}",
            row.Id, row.Type, nextRetryCount, delay.Value, result.ErrorCode, result.ErrorMessage);
    }

    private async Task HandleFailureAsync(
        BlockchainTransaction row,
        TransferBroadcastResult result,
        bool terminal,
        CancellationToken cancellationToken)
    {
        row.Status = BlockchainTransactionStatus.FAILED;
        row.NextAttemptAt = null;
        row.ErrorMessage = TrimError(result.ErrorCode, result.ErrorMessage);

        var now = _clock.GetUtcNow().UtcDateTime;
        var failedEvent = new TransferDispatchFailedEvent(
            EventId: Guid.NewGuid(),
            BlockchainTransactionId: row.Id,
            TransactionId: row.TransactionId,
            Type: row.Type,
            Token: row.Token,
            Amount: row.Amount,
            ToAddress: row.ToAddress,
            LastErrorCode: result.ErrorCode,
            LastErrorMessage: result.ErrorMessage,
            RetryCount: row.RetryCount,
            OccurredAt: now);

        await _outbox.PublishAsync(failedEvent, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogError(
            "Outbound transfer FAILED (terminal={Terminal}) — row {Id} ({Type}) after {Retry} attempts: {Code} — {Message}",
            terminal, row.Id, row.Type, row.RetryCount, result.ErrorCode, result.ErrorMessage);
    }

    private static string? TrimError(string? code, string? message)
    {
        var combined = string.IsNullOrEmpty(message) ? code : $"{code}: {message}";
        if (string.IsNullOrEmpty(combined)) return null;
        return combined.Length > 500 ? combined[..500] : combined;
    }

    // Hangfire serializes Expression<Action<T>>, so the entry point exposes a
    // synchronous wrapper that delegates to the async body on the worker.
    // [DisableConcurrentExecution] keeps a long tick from overlapping the next
    // one (see the class remarks).
    [DisableConcurrentExecution(ConcurrencyLockTimeoutSeconds)]
    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
