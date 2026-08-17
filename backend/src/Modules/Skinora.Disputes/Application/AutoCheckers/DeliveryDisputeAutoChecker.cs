using Skinora.Transactions.Application.Delivery;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Disputes.Application.AutoCheckers;

/// <summary>
/// T130 — default <see cref="IDeliveryDisputeAutoChecker"/>. Runs the 02 §9.2
/// evidence rules <b>fresh</b> through
/// <see cref="IDeliveryDisputeRound"/> and maps its verdict onto the five
/// outcomes 03 §6.2 defines.
/// </summary>
/// <remarks>
/// <para>
/// <b>What changed and why.</b> The T58/v3.0 checker read
/// <c>Transaction.DeliveryEvidence</c> off the row and tested
/// <c>IsSufficientForDelivery()</c> directly. Three defects followed from that
/// one shortcut, and all three are fixed by asking the engine instead of the
/// flags:
/// </para>
/// <list type="number">
///   <item>
///     <b>The launch-gate deadlock (T127 validation finding B5).</b> With the
///     gate closed, inventory evidence accumulates on the transaction without
///     releasing money. The old checker read that as "delivered" and closed the
///     dispute with <c>CanEscalate = false</c> — so the automatic route was
///     gated, the manual route was shut, and the buyer's funds had no exit at
///     all. The gated verdict now keeps the dispute OPEN and escalatable.
///   </item>
///   <item>
///     <b>The misdelivery signature never escalated.</b> 03 §6.2 Sonuç C says
///     the platform escalates without waiting for the buyer; the old checker
///     left the dispute OPEN and told the buyer to press a button.
///   </item>
///   <item>
///     <b>A bare flag test cannot express "I could not look".</b>
///     <c>IsMisdeliverySignature()</c> is true whenever <c>SELLER_ASSET_GONE</c>
///     is set and <c>INVENTORY_DELTA</c> is not — including when the buyer's
///     inventory was simply unreadable. The engine qualifies that with
///     <c>sellerSideKnown &amp;&amp; buyerSideKnown</c>, which is what separates
///     an accusation about a seller from an admission of ignorance (08 §2.3).
///     Every message below is now chosen from the verdict, never from a flag.
///   </item>
/// </list>
/// </remarks>
public sealed class DeliveryDisputeAutoChecker : IDeliveryDisputeAutoChecker
{
    private readonly IDeliveryDisputeRound _round;

    public DeliveryDisputeAutoChecker(IDeliveryDisputeRound round)
    {
        _round = round;
    }

    public async Task<AutoCheckResult> CheckAsync(
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var outcome = await _round.RunAsync(transaction, cancellationToken);

        return outcome switch
        {
            // Sonuç A — the round moved the transaction to ITEM_DELIVERED (or it
            // was there already). The dispute closes on the spot.
            DeliveryDisputeOutcome.Delivered =>
                Resolved(DisputeAutoCheckMessages.DeliveryDelivered),

            // Sonuç C — a positive finding about a seller. The platform does not
            // wait for the buyer to press "escalate": they may not even know
            // anything went wrong, since from their side nothing arrived.
            DeliveryDisputeOutcome.MisdeliverySignature =>
                AutoEscalated(DisputeAutoCheckMessages.DeliveryAssetGoneNotArrived),

            // Sonuç B — both sides read, neither moved.
            DeliveryDisputeOutcome.NotSent =>
                Unresolved(DisputeAutoCheckMessages.DeliveryNotSent),

            // Sonuç E — evidence found, launch gate closed. Deliberately NOT
            // auto-escalated: the gate is a launch-period review the platform
            // owner performs in bulk (DEPLOY_RUNBOOK §H.3), and routing every
            // such transaction into the admin dispute queue would confuse a
            // scheduled review with an incident. The buyer keeps the escalation
            // button, which is the exit that was missing.
            DeliveryDisputeOutcome.PendingReview =>
                Unresolved(DisputeAutoCheckMessages.DeliveryEvidenceUnderReview),

            // Sonuç D — the platform could not look.
            _ => Unresolved(DisputeAutoCheckMessages.DeliveryInventoryUnreadable),
        };
    }

    private static AutoCheckResult Resolved(string messageKey) =>
        new(Resolved: true,
            AutoEscalated: false,
            MessageKey: messageKey,
            CanSubmitTxHash: false,
            CanEscalate: false);

    private static AutoCheckResult Unresolved(string messageKey) =>
        new(Resolved: false,
            AutoEscalated: false,
            MessageKey: messageKey,
            CanSubmitTxHash: false,
            CanEscalate: true);

    private static AutoCheckResult AutoEscalated(string messageKey) =>
        new(Resolved: false,
            AutoEscalated: true,
            MessageKey: messageKey,
            CanSubmitTxHash: false,
            CanEscalate: false);
}
