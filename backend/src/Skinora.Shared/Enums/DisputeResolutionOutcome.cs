namespace Skinora.Shared.Enums;

/// <summary>
/// Admin dispute resolution decision (WP5 / T58 — 02 §10.4, 03 §6.4).
/// Drives both the terminal <see cref="DisputeStatus"/> and the transaction
/// side effect: SELLER_FAVOR upholds the transaction (clears the dispute hold
/// so the seller payout proceeds); BUYER_FAVOR unwinds it (REFUNDED + buyer
/// refund / seller item-return).
/// </summary>
public enum DisputeResolutionOutcome
{
    SELLER_FAVOR,
    BUYER_FAVOR
}
