using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Disputes.Application.AutoCheckers;

/// <summary>
/// Default <see cref="IPaymentDisputeAutoChecker"/> backed by the
/// <c>BlockchainTransactions</c> table. The <c>TransactionMonitor</c>
/// sidecar (T71) writes BUYER_PAYMENT rows as it observes confirmations
/// on Tron; the dispute pipeline simply reads the latest state per 02 §10.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>On-open path:</b> if any BUYER_PAYMENT row is already CONFIRMED for the
/// transaction, the dispute resolves as "Ödemeniz doğrulandı" and the buyer
/// is notified that the transaction continues (03 §6.1, Sonuç B).
/// </para>
/// <para>
/// <b>Submit-txhash path:</b> the buyer-supplied hash is checked against
/// existing CONFIRMED rows (case-insensitive). When no match exists the
/// auto-checker stays unresolved with the same "blockchain üzerinde ödeme
/// bulunamadı" message — the sidecar still owns the actual chain query, and
/// follow-up reconciliation happens async (T71). T58 deliberately keeps the
/// auto-checker DB-only so the dispute pipeline stays decoupled from the
/// sidecar HTTP surface; the sidecar can then upgrade the dispute by
/// retroactively writing a CONFIRMED BUYER_PAYMENT row + emitting a
/// downstream DisputeAutoResolvedEvent (T71 follow-up).
/// </para>
/// </remarks>
public sealed class PaymentDisputeAutoChecker : IPaymentDisputeAutoChecker
{
    private const string ResolvedMessage = "Ödemeniz doğrulandı, işlem devam ediyor";
    private const string UnresolvedMessage = "Blockchain üzerinde ödeme bulunamadı";

    private readonly AppDbContext _db;

    public PaymentDisputeAutoChecker(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AutoCheckResult> CheckAsync(
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var hasConfirmedPayment = await _db.Set<BlockchainTransaction>()
            .AnyAsync(
                bt => bt.TransactionId == transaction.Id
                    && bt.Type == BlockchainTransactionType.BUYER_PAYMENT
                    && bt.Status == BlockchainTransactionStatus.CONFIRMED,
                cancellationToken);

        return hasConfirmedPayment
            ? new AutoCheckResult(
                Resolved: true,
                AutoEscalated: false,
                Message: ResolvedMessage,
                CanSubmitTxHash: false,
                CanEscalate: false)
            : new AutoCheckResult(
                Resolved: false,
                AutoEscalated: false,
                Message: UnresolvedMessage,
                CanSubmitTxHash: true,
                CanEscalate: true);
    }

    public async Task<AutoCheckResult> CheckWithTxHashAsync(
        Transaction transaction,
        string txHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(txHash);

        // Normalize both sides via LOWER() — translates on SQL Server and
        // SQLite identically, so case-insensitive Tron tx hash matching works
        // under integration tests too. Sidecar callers (T71) write hashes in
        // canonical lowercase, but submit-txhash receives buyer input that
        // may be uppercase or mixed-case from explorer copy-paste flows.
        var normalizedHash = txHash.Trim().ToLowerInvariant();

        var match = await _db.Set<BlockchainTransaction>()
            .Where(
                bt => bt.TransactionId == transaction.Id
                    && bt.Type == BlockchainTransactionType.BUYER_PAYMENT
                    && bt.Status == BlockchainTransactionStatus.CONFIRMED
                    && bt.TxHash != null
                    && bt.TxHash.ToLower() == normalizedHash)
            .AnyAsync(cancellationToken);

        return match
            ? new AutoCheckResult(
                Resolved: true,
                AutoEscalated: false,
                Message: ResolvedMessage,
                CanSubmitTxHash: false,
                CanEscalate: false)
            : new AutoCheckResult(
                Resolved: false,
                AutoEscalated: false,
                Message: UnresolvedMessage,
                CanSubmitTxHash: true,
                CanEscalate: true);
    }
}
