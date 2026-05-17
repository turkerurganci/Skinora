using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
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
    ];

    private readonly AppDbContext _db;
    private readonly IBlockchainTransferClient _client;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutgoingTransferConfirmationJob> _logger;

    public OutgoingTransferConfirmationJob(
        AppDbContext db,
        IBlockchainTransferClient client,
        TimeProvider clock,
        ILogger<OutgoingTransferConfirmationJob> logger)
    {
        _db = db;
        _client = client;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var candidateIds = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(b => b.Status == BlockchainTransactionStatus.DETECTED
                && b.TxHash != null
                && OutboundTypes.Contains(b.Type))
            .OrderBy(b => b.CreatedAt)
            .Take(BatchSize)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0) return;

        foreach (var id in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ConfirmOneAsync(id, cancellationToken);
        }
    }

    private async Task ConfirmOneAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _db.Set<BlockchainTransaction>()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (row is null || row.Status != BlockchainTransactionStatus.DETECTED || row.TxHash is null) return;

        var status = await _client.GetStatusAsync(row.TxHash, cancellationToken);
        switch (status.Outcome)
        {
            case TransferStatusOutcome.Confirmed:
                row.Status = BlockchainTransactionStatus.CONFIRMED;
                row.BlockNumber = status.BlockNumber;
                row.ConfirmationCount = status.Confirmations ?? 20;
                row.ConfirmedAt = _clock.GetUtcNow().UtcDateTime;
                row.ErrorMessage = null;
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Outbound transfer CONFIRMED — row {Id} ({Type}) tx {TxHash} @ block {Block}",
                    row.Id, row.Type, row.TxHash, status.BlockNumber);
                return;

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
                return;

            case TransferStatusOutcome.Unavailable:
                _logger.LogWarning(
                    "Outbound transfer status unavailable — row {Id} tx {TxHash}: {Message}",
                    row.Id, row.TxHash, status.ErrorMessage);
                return;

            case TransferStatusOutcome.Pending:
            default:
                // Solidity node has not caught up — keep DETECTED, no row mutation.
                return;
        }
    }

    private static string? TruncateError(string? message)
    {
        if (string.IsNullOrEmpty(message)) return null;
        return message.Length > 500 ? message[..500] : message;
    }

    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
