using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
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
/// </summary>
public sealed class OutgoingTransferDispatchJob
{
    public const string RecurringJobId = "outgoing-transfer-dispatch";

    /// <summary>Cron — every minute. Mirrors <c>EnsurePaymentAddressJob.Cron</c>.</summary>
    public const string Cron = "* * * * *";

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
    ];

    private readonly AppDbContext _db;
    private readonly IBlockchainTransferClient _client;
    private readonly ITransferRetryPolicy _retryPolicy;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutgoingTransferDispatchJob> _logger;

    public OutgoingTransferDispatchJob(
        AppDbContext db,
        IBlockchainTransferClient client,
        ITransferRetryPolicy retryPolicy,
        IOutboxService outbox,
        TimeProvider clock,
        ILogger<OutgoingTransferDispatchJob> logger)
    {
        _db = db;
        _client = client;
        _retryPolicy = retryPolicy;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var candidateIds = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(b => b.Status == BlockchainTransactionStatus.PENDING
                && OutboundTypes.Contains(b.Type)
                && (b.NextAttemptAt == null || b.NextAttemptAt <= now))
            .OrderBy(b => b.CreatedAt)
            .Take(BatchSize)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0) return;

        _logger.LogInformation(
            "OutgoingTransferDispatchJob picked up {Count} eligible rows", candidateIds.Count);

        foreach (var id in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchOneAsync(id, cancellationToken);
        }
    }

    private async Task DispatchOneAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _db.Set<BlockchainTransaction>()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (row is null || row.Status != BlockchainTransactionStatus.PENDING) return;

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
                    "Dispatcher could not resolve deposit address for refund row {Id} (transaction {TransactionId}) — skipping",
                    row.Id, row.TransactionId);
                return;
            }
            var addressRow = await _db.Set<PaymentAddress>()
                .AsNoTracking()
                .Where(p => p.Id == sourcePayment.PaymentAddressId.Value)
                .Select(p => new { p.Address, p.HdWalletIndex })
                .FirstOrDefaultAsync(cancellationToken);
            if (addressRow is null)
            {
                _logger.LogWarning(
                    "Dispatcher could not resolve PaymentAddress row {PaymentAddressId} for refund {Id}",
                    sourcePayment.PaymentAddressId, row.Id);
                return;
            }
            depositIndex = addressRow.HdWalletIndex;
            depositAddress = addressRow.Address;
            row.FromAddress = addressRow.Address;
        }

        var request = new TransferBroadcastRequest(
            BlockchainTransactionId: row.Id,
            Type: row.Type,
            Token: row.Token,
            Amount: row.Amount,
            ToAddress: row.ToAddress,
            DepositIndex: depositIndex,
            DepositAddress: depositAddress);

        var result = await _client.BroadcastAsync(request, cancellationToken);
        switch (result.Status)
        {
            case TransferBroadcastStatus.Success:
                await HandleSuccessAsync(row, result, cancellationToken);
                return;

            case TransferBroadcastStatus.InvalidRequest:
                // Sidecar refused the payload outright — no further attempts
                // help. Flip to FAILED and alert immediately.
                await HandleFailureAsync(row, result, terminal: true, cancellationToken);
                return;

            case TransferBroadcastStatus.TransientFailure:
            default:
                await HandleTransientAsync(row, result, cancellationToken);
                return;
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
    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
