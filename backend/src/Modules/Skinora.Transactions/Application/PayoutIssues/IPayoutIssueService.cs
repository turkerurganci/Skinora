namespace Skinora.Transactions.Application.PayoutIssues;

/// <summary>
/// T60 — seller payout-issue pipeline (07 §7.11, 02 §10.3, 06 §3.8a, 03 §2.4a
/// Senaryo A). The seller invokes this from a COMPLETED transaction's detail
/// page when they did not receive their payout despite the platform recording
/// a successful broadcast.
/// </summary>
/// <remarks>
/// Senaryo B (ITEM_DELIVERED + stuck payout, pre-COMPLETED) is owned by the
/// blockchain payout retry mechanism (06 §3.8 BlockchainTransaction.RetryCount,
/// 05 §3.3 exponential backoff 1m/5m/15m) and is not exposed via this service.
/// </remarks>
public interface IPayoutIssueService
{
    /// <summary>
    /// Persists a SellerPayoutIssue row at REPORTED, runs the verifier, applies
    /// the resulting state transition (RESOLVED / ESCALATED / RETRY_SCHEDULED),
    /// and emits the matching outbox event — all atomic with a single
    /// SaveChanges call.
    /// </summary>
    Task<ReportPayoutIssueOutcome> ReportAsync(
        Guid callerUserId,
        Guid transactionId,
        ReportPayoutIssueRequest request,
        CancellationToken cancellationToken);
}
