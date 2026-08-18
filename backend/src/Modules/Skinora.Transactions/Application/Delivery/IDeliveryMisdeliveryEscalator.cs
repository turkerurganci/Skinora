using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// T127 — port for raising the 02 §10.1 / 03 §4.4 admin escalation when a
/// delivery-verification round finds the misdelivery signature: the seller's
/// asset left their inventory but nothing reached the buyer.
/// </summary>
/// <remarks>
/// <para>
/// A port rather than a direct call because the module dependency runs
/// <c>Disputes → Transactions</c>: the escalation is a Dispute row, and this
/// assembly cannot see that type. The adapter lives in
/// <c>Skinora.Disputes</c>, mirroring how
/// <see cref="Skinora.Transactions.Application.Steam.ISteamInventoryReader"/> is
/// declared here and implemented in <c>Skinora.Steam</c>.
/// </para>
/// <para>
/// <b>Unit of work.</b> Implementations add their rows to the caller's tracked
/// <c>AppDbContext</c> and must NOT call <c>SaveChangesAsync</c>. The escalation
/// and the evidence capture that justifies it commit together or not at all —
/// a transaction whose capture says "the item went somewhere else" with no
/// dispute attached is exactly the silent cancellation 02 §9.2 forbids.
/// </para>
/// </remarks>
public interface IDeliveryMisdeliveryEscalator
{
    /// <summary>
    /// Escalate <paramref name="transaction"/> to admin review.
    /// </summary>
    /// <remarks>
    /// Idempotent: 02 §10.2 allows only one dispute per (transaction, type) and
    /// <c>UQ_Disputes_TransactionId_Type</c> is unfiltered, so an implementation
    /// promotes an existing DELIVERY dispute instead of inserting a second row.
    /// </remarks>
    /// <param name="transaction">The transaction whose delivery round found the signature.</param>
    /// <param name="occurredAtUtc">This round's clock reading.</param>
    /// <param name="signatureFirstObservedAtUtc">
    /// When the misdelivery signature this escalation is about was FIRST
    /// established — the recorded capture's observation time on a re-entry, or
    /// <paramref name="occurredAtUtc"/> on the round that establishes it
    /// (T131 validation finding N2).
    /// <para>
    /// The implementation needs it to answer one question an outcome alone
    /// cannot: whether the admin who already ruled had this signature in front
    /// of them. A ruling that PREDATES the signature was made without it, so
    /// it cannot be the human reading that 02 §9.2 requires before a
    /// cancellation stops being silent.
    /// </para>
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<MisdeliveryEscalationOutcome> EscalateAsync(
        Transaction transaction,
        DateTime occurredAtUtc,
        DateTime signatureFirstObservedAtUtc,
        CancellationToken cancellationToken);
}

/// <summary>What one escalation attempt did — diagnostics for the caller's log.</summary>
public enum MisdeliveryEscalationOutcome
{
    /// <summary>No DELIVERY dispute existed; one was inserted as ESCALATED.</summary>
    Opened,

    /// <summary>The buyer had already opened one; it was promoted to ESCALATED.</summary>
    Promoted,

    /// <summary>A DELIVERY dispute was already ESCALATED — nothing to do.</summary>
    AlreadyEscalated,

    /// <summary>
    /// A DELIVERY dispute exists in CLOSED — the system's own auto-check
    /// answered it, no human looked. Left alone: the unfiltered unique index
    /// forbids a second row, and re-opening a settled decision is not this
    /// job's call.
    /// </summary>
    AlreadyResolved,

    /// <summary>
    /// T131 — a DELIVERY dispute exists in an ADMIN resolution
    /// (RESOLVED_FOR_SELLER / RESOLVED_FOR_BUYER): a human has read this
    /// transaction and ruled on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinguished from <see cref="AlreadyResolved"/> because it is the one
    /// signal that releases the caller's hold. 02 §9.2 forbids cancelling a
    /// misdelivery signature <em>silently</em>; once an admin has ruled, a
    /// cancellation is no longer silent, and continuing to hold would leave the
    /// buyer's money in escrow forever with no automatic exit (T127 finding
    /// G3). CLOSED does not qualify — that terminal is reserved for the
    /// system's own auto-resolution (06 §2.10), so nobody has looked.
    /// </para>
    /// <para>
    /// Returned only when the ruling came AFTER the signature was recorded, so
    /// the admin had it in front of them. The other order is
    /// <see cref="ReEscalatedAfterRuling"/>.
    /// </para>
    /// </remarks>
    AlreadyRuledByAdmin,

    /// <summary>
    /// T131 (validation finding N2) — an admin resolution exists, but it
    /// PREDATES the misdelivery signature: the ruling was made without this
    /// evidence. The dispute was put back into the admin queue (ESCALATED) with
    /// the signature's result text, and the caller must keep holding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The release <see cref="AlreadyRuledByAdmin"/> grants rests entirely on a
    /// human having read the case; here nobody read THIS. Releasing anyway
    /// cancels the transaction and refunds the buyer — quietly reversing a
    /// seller-favour ruling on evidence its author never saw, and leaving a
    /// seller whose item has already left their inventory without the item and
    /// without the money.
    /// </para>
    /// <para>
    /// Re-escalating rather than holding in silence: 02 §10.2's rule forbids a
    /// SECOND dispute row for the same (transaction, type), and this is the same
    /// row. What arrived is a new fact — the platform watched the item leave
    /// after the ruling — not a re-litigation of the old one, and the
    /// alternative (hold forever, exit only through a manual AD19) is the
    /// fail-frozen shape T131's own D4 decision exists to close.
    /// </para>
    /// </remarks>
    ReEscalatedAfterRuling,
}
