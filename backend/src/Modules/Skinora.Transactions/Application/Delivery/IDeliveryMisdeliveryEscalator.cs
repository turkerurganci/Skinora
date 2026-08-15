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
    Task<MisdeliveryEscalationOutcome> EscalateAsync(
        Transaction transaction,
        DateTime occurredAtUtc,
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
    /// A DELIVERY dispute exists in a resolved terminal (CLOSED or an admin
    /// resolution). Left alone: the unfiltered unique index forbids a second
    /// row, and re-opening a decision a human already made is not this job's
    /// call.
    /// </summary>
    AlreadyResolved,
}
