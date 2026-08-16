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

    /// <summary>
    /// Stage an ACCOUNT-level <c>FraudFlag</c> row (T129 — 02 §4.5.1: "satıcı
    /// hesabına dolandırıcılık işareti konur", §14.2 counts the repeat).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Account-level rather than transaction-scoped, and that is the document's
    /// word rather than a convenience: the finding is about the person, the
    /// sanction in §14.2 is about their history, and a transaction-scoped flag
    /// would be reviewed through the pre-create approve/reject path whose
    /// outcomes (FLAGGED → CREATED / CANCELLED_ADMIN) are meaningless on a
    /// transaction that has already refunded. 06 §3.12 enforces the shape from
    /// the other side: <c>ACCOUNT_LEVEL</c> requires <c>TransactionId IS NULL</c>,
    /// so the originating transaction travels in the details payload.
    /// </para>
    /// <para>
    /// No emergency-hold cascade. That escalation belongs to sanctions matching
    /// (02 §14.0, T82), where the platform is legally obliged to stop every
    /// movement at once; a single reversal is a finding for a human to weigh,
    /// and freezing the seller's unrelated in-flight transactions on it would
    /// punish counter-parties who have nothing to do with this one.
    /// </para>
    /// </remarks>
    Task StageAccountFlagAsync(
        Guid userId,
        FraudFlagType type,
        string details,
        CancellationToken cancellationToken);
}
