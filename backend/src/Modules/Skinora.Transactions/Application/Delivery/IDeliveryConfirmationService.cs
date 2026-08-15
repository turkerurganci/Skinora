namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// T126 — orchestrates <c>POST /transactions/:id/confirm-receipt</c>
/// (07 §7.6b, 03 §3.5 step 6): the buyer states the item arrived, and the
/// transaction moves <c>PAYMENT_RECEIVED → ITEM_DELIVERED</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the first production caller of <c>TransactionTrigger.DeliverItem</c>
/// — until now <c>PAYMENT_RECEIVED</c> could only be left by a cancellation or a
/// timeout, so the delivery leg of the P2P lifecycle had no entry point at all.
/// </para>
/// <para>
/// <b>Why the buyer's word is enough.</b> 02 §9.2 accepts buyer confirmation as
/// sufficient evidence on its own because it runs <em>against</em> the buyer's
/// own interest: confirming releases their escrowed money to the seller. There
/// is no incentive to claim it falsely, which is what distinguishes it from the
/// platform's inventory inference — and why it is the one evidence path the
/// launch gate (DEPLOY_RUNBOOK §H) does not restrain.
/// </para>
/// <para>
/// <b>The round still runs.</b> 02 §9.2 requires the evidence rules to execute
/// when the buyer confirms, so this service delegates to
/// <see cref="IDeliveryVerificationService"/> rather than re-deriving them.
/// <c>BUYER_CONFIRMED</c> is merged onto the transaction <em>before</em> the
/// round, which makes the engine short-circuit: no Steam reads, verdict
/// <c>Delivered</c>, <c>AutoReleaseGated</c> false. That ordering is deliberate
/// on two counts — T125 documents that reading Steam after a buyer confirmation
/// could only produce a weaker signal arguing with a stronger one, and it is
/// what keeps the F3 launch-gate invariant mechanical instead of conditional
/// (see <c>DeliveryConfirmationService</c>).
/// </para>
/// </remarks>
public interface IDeliveryConfirmationService
{
    Task<ConfirmReceiptOutcome> ConfirmReceiptAsync(
        Guid buyerId,
        Guid transactionId,
        CancellationToken cancellationToken);
}
