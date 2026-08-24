namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Read port over the <c>Dispute</c> rows of a single transaction — backs the
/// 07 §7.5 <c>dispute</c> block (WP6b, T133a-DisputeBlockNulls).
/// </summary>
/// <remarks>
/// The implementation lives in <c>Skinora.Disputes</c> because that module owns
/// the entity; <c>Skinora.Transactions</c> stays free of a Disputes project
/// reference, which would close the cycle Disputes → Transactions → Disputes.
/// Same shape and rationale as <see cref="IAccountFlagChecker"/>.
/// </remarks>
public interface ITransactionDisputeSummaryProvider
{
    /// <summary>
    /// Returns the most recent dispute on the transaction, or <c>null</c> when
    /// it has none.
    /// </summary>
    /// <remarks>
    /// Most recent, not "the open one": 02 §10.2 allows one dispute per type, so
    /// a transaction can carry several, and the summary describes the one the
    /// party is currently acting on.
    /// </remarks>
    Task<DisputeSummaryDto?> GetLatestAsync(Guid transactionId, CancellationToken cancellationToken);
}
