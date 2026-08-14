namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// T123 — orchestrates <c>POST /transactions/:id/confirm-ready</c>
/// (07 §7.6a, 03 §2.3): the seller declares the item ready to send, and the
/// platform opens the payment window.
/// </summary>
/// <remarks>
/// <para>
/// This step exists to stop the buyer paying into a stale listing. The item was
/// last verified when the transaction was created — possibly hours earlier —
/// and in the P2P model it never leaves the seller's account, so it can be sold,
/// traded or locked in the meantime. Three checks run before the deposit address
/// is revealed (03 §2.3 step 3): the item is still in the seller's inventory and
/// tradeable, the buyer's Mobile Authenticator is still active, and — best
/// effort — the buyer's inventory is snapshotted as the 02 §9.2 delivery
/// baseline.
/// </para>
/// <para>
/// The first two are blocking; the third is not. An unreadable buyer inventory
/// closes the inventory-evidence path but leaves buyer confirmation intact, and
/// refusing the transaction over it would punish both parties for a privacy
/// setting the seller cannot change (02 §9.2).
/// </para>
/// <para>
/// The baseline is taken <em>here</em> rather than at payment confirmation on
/// purpose: a seller can technically send the item before being paid, and a
/// later baseline would absorb that item, leaving the delta permanently
/// invisible (03 §2.3 note).
/// </para>
/// </remarks>
public interface ITransactionReadinessService
{
    Task<ConfirmReadyOutcome> ConfirmReadyAsync(
        Guid sellerId,
        Guid transactionId,
        CancellationToken cancellationToken);
}
