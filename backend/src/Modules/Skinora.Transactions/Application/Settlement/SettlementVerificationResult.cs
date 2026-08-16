using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Steam;

namespace Skinora.Transactions.Application.Settlement;

/// <summary>
/// The end-of-window verdict (T129 — 02 §4.5.1, 03 §2.4 step 2).
/// </summary>
/// <remarks>
/// 03 §2.4 states three branches (item there / item gone / cannot read). This
/// enum has four, because "item gone" is not one fact but two: a trade that was
/// reversed puts the item back with the SELLER, while a buyer who sold the skin
/// onward leaves it nowhere the platform can see. Both look identical from the
/// buyer's side alone, and they call for opposite actions — refund the buyer, or
/// pay the seller. Collapsing them would hand the buyer the mirror image of the
/// fraud this whole mechanism exists to stop (owner decision, 2026-08-16).
/// </remarks>
public enum SettlementVerdict
{
    /// <summary>
    /// The item is still with the buyer. The trade stands → stamp
    /// <c>SettlementVerifiedAt</c> and let the payout flow.
    /// </summary>
    Verified,

    /// <summary>
    /// The item left the buyer AND is back with the seller — the signature of a
    /// reversed trade. Whether this moves money on its own depends on the launch
    /// gate (<c>settlement.reversal_auto_refund_enabled</c>).
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
    /// A side could not be read, or the delivery left no reference to measure
    /// against. Not a finding (08 §2.3): retried, and escalated to an admin once
    /// it persists past <c>settlement.unreadable_escalation_hours</c>.
    /// </summary>
    Inconclusive,
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
/// Whether the seller's original asset id is back in their inventory — the one
/// signal that separates a reversal from an onward sale. <c>null</c> when the
/// seller side was not read (the buyer still holds the item, so there was
/// nothing to disambiguate) or could not be read.
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
