using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Steam;

namespace Skinora.Transactions.Application.Settlement;

/// <summary>
/// The end-of-window verdict (T129 — 02 §4.5.1, 03 §2.4 step 2).
/// </summary>
/// <remarks>
/// <para>
/// 03 §2.4 states three branches (item there / item gone / cannot read). This
/// enum has five. "Item gone" is not one fact but two: a trade that was
/// reversed puts the item back with the SELLER, while a buyer who sold the skin
/// onward leaves it nowhere the platform can see. Both look identical from the
/// buyer's side alone, and they call for opposite actions — refund the buyer, or
/// pay the seller. Collapsing them would hand the buyer the mirror image of the
/// fraud this whole mechanism exists to stop (owner decision, 2026-08-16).
/// </para>
/// <para>
/// "Cannot read" is likewise two facts, and the fix round split them: an
/// inventory that is closed TODAY may open tomorrow, but a delivery that
/// recorded no asset id and had no baseline can never be measured, however many
/// times it is retried. Treating the second as the first froze those
/// transactions forever — payout, sweep and COMPLETED all wait on a stamp that
/// could not arrive, with no admin lever to end it (validator finding B1).
/// </para>
/// </remarks>
public enum SettlementVerdict
{
    /// <summary>
    /// The item is still with the buyer. The trade stands → stamp
    /// <c>SettlementVerifiedAt</c> and let the payout flow.
    /// </summary>
    Verified,

    /// <summary>
    /// The item left the buyer AND came BACK to the seller — the signature of a
    /// reversed trade. Both halves are required: the delivery must have observed
    /// the asset leave the seller (<c>DeliveryEvidence.SELLER_ASSET_GONE</c>) and
    /// the settlement read must find it there again. Whether this moves money on
    /// its own depends on the launch gate
    /// (<c>settlement.reversal_auto_refund_enabled</c>).
    /// </summary>
    ReversalSignature,

    /// <summary>
    /// The item left the buyer, but nothing says it went back to the seller. The
    /// buyer may have traded it on (Steam's 7-day restriction expires one day
    /// before the default window closes). Never auto-decided in either
    /// direction — a human is asked.
    /// </summary>
    AmbiguousDeparture,

    /// <summary>
    /// A side could not be read this round. Not a finding (08 §2.3): retried,
    /// and escalated to an admin once it persists past
    /// <c>settlement.unreadable_escalation_hours</c>. The inventory may open
    /// later, so retrying can genuinely win.
    /// </summary>
    Inconclusive,

    /// <summary>
    /// The delivery left nothing to measure against — no
    /// <c>DeliveredBuyerAssetId</c> and no buyer baseline — so the check has no
    /// decision input and no round will ever produce one. Escalated
    /// immediately, without waiting out the unreadable threshold: waiting only
    /// delays a seller who cannot be judged either way.
    /// </summary>
    /// <remarks>
    /// Reached by every transaction whose buyer inventory was private at
    /// SELLER_CONFIRMED (baseline deliberately left NULL — 03 §2.3, 06 §3.5) and
    /// whose delivery then closed on the buyer's own confirmation (which reads
    /// no inventory, so no asset id is recorded). Neither column is writable
    /// after ITEM_DELIVERED. Distinct from <see cref="Inconclusive"/> precisely
    /// because it is permanent, and the admin procedure differs accordingly
    /// (DEPLOY_RUNBOOK §I.5, not §I.3).
    /// </remarks>
    NoDeliveryReference,
}

/// <summary>
/// One settlement re-check, with the reads that produced it.
/// </summary>
/// <param name="Verdict">The 02 §4.5.1 conclusion.</param>
/// <param name="BuyerHoldsItem">
/// Tri-state: <c>true</c> the item was found with the buyer, <c>false</c> it was
/// provably absent from a readable inventory, <c>null</c> the buyer side could
/// not be established at all.
/// </param>
/// <param name="SellerAssetReturned">
/// Whether the seller's original asset id is back in their inventory — half of
/// the signal that separates a reversal from an onward sale; the other half is
/// the delivery-time observation that it had LEFT (see the
/// <c>SELLER_ASSET_GONE</c> test in the service). <c>null</c> when the seller
/// side was not read (the buyer still holds the item, so there was nothing to
/// disambiguate) or could not be read.
/// </param>
/// <param name="BuyerVisibility">Buyer-side read outcome, null when not read.</param>
/// <param name="SellerVisibility">Seller-side read outcome, null when not read.</param>
/// <param name="ObservedClassCount">
/// Buyer's observed count of the traded item class, when the count route was
/// used (no <c>DeliveredBuyerAssetId</c> to test exactly).
/// </param>
/// <param name="ExpectedClassCount">
/// The count the delivery established — baseline + 1. Null when there was no
/// baseline to build on.
/// </param>
/// <param name="Detail">Human-readable summary for the audit / admin surface.</param>
public sealed record SettlementVerificationResult(
    SettlementVerdict Verdict,
    bool? BuyerHoldsItem,
    bool? SellerAssetReturned,
    InventoryVisibility? BuyerVisibility,
    InventoryVisibility? SellerVisibility,
    int? ObservedClassCount,
    int? ExpectedClassCount,
    string Detail);
