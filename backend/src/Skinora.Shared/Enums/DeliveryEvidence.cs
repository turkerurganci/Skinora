namespace Skinora.Shared.Enums;

/// <summary>
/// Which proofs of delivery have been observed (02 §9.2, 06 §2.24).
/// <para>
/// The platform is not a party to the seller-to-buyer trade, so Steam never
/// tells it "the offer was accepted". Delivery is therefore inferred from two
/// independent sources, and this flag records which ones fired.
/// </para>
/// </summary>
[Flags]
public enum DeliveryEvidence
{
    NONE = 0,

    /// <summary>
    /// The buyer pressed "I received it". Sufficient on its own: the
    /// confirmation is against the buyer's own interest (it releases their
    /// money to the seller), so there is no incentive to claim it falsely.
    /// </summary>
    BUYER_CONFIRMED = 1,

    /// <summary>
    /// The expected (ClassId, InstanceId) count in the buyer's inventory rose
    /// above the baseline captured at SELLER_CONFIRMED. Not sufficient alone —
    /// the buyer may have acquired the same skin elsewhere.
    /// </summary>
    INVENTORY_DELTA = 2,

    /// <summary>
    /// The seller's specific ItemAssetId left their inventory. Not sufficient
    /// alone — the seller may have sent it to a third party.
    /// </summary>
    SELLER_ASSET_GONE = 4,
}

public static class DeliveryEvidenceExtensions
{
    /// <summary>
    /// Whether the observed evidence is enough to move to ITEM_DELIVERED
    /// (02 §9.2):
    /// <code>
    /// deliver ⟸ BUYER_CONFIRMED
    /// deliver ⟸ SELLER_ASSET_GONE AND INVENTORY_DELTA
    /// </code>
    /// The conjunction is required because either half alone produces a wrong
    /// answer in a realistic case, and both wrong answers move real money.
    /// </summary>
    public static bool IsSufficientForDelivery(this DeliveryEvidence evidence)
    {
        if (evidence.HasFlag(DeliveryEvidence.BUYER_CONFIRMED))
        {
            return true;
        }

        return evidence.HasFlag(DeliveryEvidence.SELLER_ASSET_GONE)
            && evidence.HasFlag(DeliveryEvidence.INVENTORY_DELTA);
    }

    /// <summary>
    /// The fraud signature: the item left the seller but never reached the
    /// buyer. Means a wrong item was sent, or it went to a third party. Such a
    /// transaction must never be cancelled silently — it is escalated to an
    /// admin instead (02 §10.1, 03 §6.2).
    /// </summary>
    public static bool IsMisdeliverySignature(this DeliveryEvidence evidence)
        => evidence.HasFlag(DeliveryEvidence.SELLER_ASSET_GONE)
            && !evidence.HasFlag(DeliveryEvidence.INVENTORY_DELTA);
}
