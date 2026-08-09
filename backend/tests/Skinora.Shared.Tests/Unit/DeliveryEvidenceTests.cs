using Skinora.Shared.Enums;

namespace Skinora.Shared.Tests.Unit;

/// <summary>
/// 02 §9.2 / 06 §2.24 — the delivery evidence rules. Both branches move real
/// money (a false positive pays a seller who never delivered; a false negative
/// refunds a buyer who did receive the item), so every combination of the three
/// bits is pinned rather than only the interesting ones.
/// </summary>
public class DeliveryEvidenceTests
{
    private const DeliveryEvidence Buyer = DeliveryEvidence.BUYER_CONFIRMED;
    private const DeliveryEvidence Delta = DeliveryEvidence.INVENTORY_DELTA;
    private const DeliveryEvidence Gone = DeliveryEvidence.SELLER_ASSET_GONE;

    // deliver ⟸ BUYER_CONFIRMED
    // deliver ⟸ SELLER_ASSET_GONE AND INVENTORY_DELTA
    [Theory]
    [InlineData(DeliveryEvidence.NONE, false)]
    [InlineData(Buyer, true)]
    [InlineData(Delta, false)]                  // buyer may have got the skin elsewhere
    [InlineData(Gone, false)]                   // seller may have sent it to a third party
    [InlineData(Delta | Gone, true)]
    [InlineData(Buyer | Delta, true)]
    [InlineData(Buyer | Gone, true)]
    [InlineData(Buyer | Delta | Gone, true)]
    public void IsSufficientForDelivery_MatchesEvidenceMatrix(DeliveryEvidence evidence, bool expected)
    {
        Assert.Equal(expected, evidence.IsSufficientForDelivery());
    }

    // The misdelivery signature is exactly SELLER_ASSET_GONE without
    // INVENTORY_DELTA: the item left the seller and never arrived. It must be
    // escalated, never cancelled silently (02 §10.1).
    [Theory]
    [InlineData(DeliveryEvidence.NONE, false)]
    [InlineData(Buyer, false)]
    [InlineData(Delta, false)]
    [InlineData(Gone, true)]
    [InlineData(Delta | Gone, false)]
    [InlineData(Buyer | Gone, true)]
    [InlineData(Buyer | Delta | Gone, false)]
    public void IsMisdeliverySignature_MatchesEvidenceMatrix(DeliveryEvidence evidence, bool expected)
    {
        Assert.Equal(expected, evidence.IsMisdeliverySignature());
    }

    [Fact]
    public void InventoryDelta_AloneIsNeverSufficient_EvenCombinedWithNothingElse()
    {
        // Guards the single most expensive mistake available here: paying a
        // seller because the buyer happened to acquire the same skin elsewhere.
        Assert.False(Delta.IsSufficientForDelivery());
    }
}
