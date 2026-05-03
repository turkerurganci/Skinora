using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Write port for the pre-create transaction flag persisted alongside a
/// FLAGGED transaction (T54 — 02 §14.0, 03 §7.1, 06 §3.12). The
/// implementation lives in <c>Skinora.Fraud</c>; the port is declared here
/// so <c>Skinora.Transactions</c> stays free of a Skinora.Fraud project
/// reference (avoids the would-be cycle Fraud → Transactions → Fraud,
/// matching the existing <see cref="IAccountFlagChecker"/> pattern).
/// </summary>
/// <remarks>
/// Atomicity boundary: this method only stages an <c>Add</c> on the change
/// tracker. The caller (<c>TransactionCreationService</c>) owns the
/// SaveChanges so the inserted <c>Transaction</c> with <c>Status=FLAGGED</c>
/// and the matching <c>FraudFlag</c> row commit together — an admin can
/// never observe a flagged transaction without its flag.
/// </remarks>
public interface ITransactionFraudFlagWriter
{
    /// <summary>
    /// Stage a <c>FraudFlag</c> row for the supplied flagged transaction.
    /// </summary>
    /// <param name="userId">Seller user id (the seller initiates the transaction).</param>
    /// <param name="transactionId">Identifier of the flagged transaction (already <c>Add</c>ed).</param>
    /// <param name="type">Type of fraud signal that produced the flag.</param>
    /// <param name="details">JSON payload — type-specific shape (07 §9.3).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StagePreCreateFlagAsync(
        Guid userId,
        Guid transactionId,
        FraudFlagType type,
        string details,
        CancellationToken cancellationToken);
}
