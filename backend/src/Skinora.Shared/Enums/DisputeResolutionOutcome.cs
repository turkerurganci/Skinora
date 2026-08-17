namespace Skinora.Shared.Enums;

/// <summary>
/// Admin dispute resolution decision (WP5 / T58 — 02 §10.4, 03 §6.4).
/// Drives both the terminal <see cref="DisputeStatus"/> and the transaction
/// side effect: SELLER_FAVOR upholds the transaction (clears the dispute hold
/// so the seller payout proceeds at ITEM_DELIVERED); BUYER_FAVOR unwinds it
/// (REFUNDED + buyer payment refund).
/// </summary>
/// <remarks>
/// A buyer-favour ruling moves money and nothing else. There is no item leg
/// and never was one in v3.0: the platform is not a party to the seller-buyer
/// trade and never holds the item, so it has nothing to return (02 §3.2 —
/// "Item iadesi diye bir işlem yoktur"). Where the item sits when the ruling
/// lands is where it stays.
/// </remarks>
public enum DisputeResolutionOutcome
{
    SELLER_FAVOR,
    BUYER_FAVOR
}
