using Skinora.Disputes.Application.AutoCheckers;
using Skinora.Shared.Enums;
using Skinora.Transactions.Domain.Entities;
using Xunit;

namespace Skinora.Disputes.Tests.Unit;

/// <summary>
/// T117 (validator) — the delivery auto-checker decides from
/// <see cref="DeliveryEvidence"/> only (02 §9.2). These tests pin both the
/// outcome and the message key, because the message is what the buyer reads
/// when they are told whether their item arrived.
/// </summary>
[Trait("Category", "Unit")]
public class DeliveryDisputeAutoCheckerTests
{
    private static Transaction WithEvidence(DeliveryEvidence evidence) =>
        new() { Id = Guid.NewGuid(), DeliveryEvidence = evidence };

    [Fact]
    public async Task SufficientEvidence_ResolvesAsDelivered()
    {
        var result = await new DeliveryDisputeAutoChecker()
            .CheckAsync(WithEvidence(DeliveryEvidence.BUYER_CONFIRMED), CancellationToken.None);

        Assert.True(result.Resolved);
        Assert.Equal(DisputeAutoCheckMessages.DeliveryDelivered, result.MessageKey);
    }

    /// <summary>
    /// The seller's asset is gone but nothing arrived — a wrong item or a send
    /// to a third party (02 §10.1). The dispute must stay open AND the message
    /// must describe that situation. It previously rendered "your trade offer is
    /// active, accept it on Steam", which is impossible to act on in the P2P
    /// model and reads as reassurance in the one case that warrants an admin.
    /// </summary>
    [Fact]
    public async Task MisdeliverySignature_StaysOpen_AndNamesTheSituation()
    {
        var result = await new DeliveryDisputeAutoChecker()
            .CheckAsync(WithEvidence(DeliveryEvidence.SELLER_ASSET_GONE), CancellationToken.None);

        Assert.False(result.Resolved);
        Assert.True(result.CanEscalate);
        Assert.Equal(DisputeAutoCheckMessages.DeliveryAssetGoneNotArrived, result.MessageKey);
    }

    [Fact]
    public async Task NoEvidence_StaysOpen_AsNotSentYet()
    {
        var result = await new DeliveryDisputeAutoChecker()
            .CheckAsync(WithEvidence(DeliveryEvidence.NONE), CancellationToken.None);

        Assert.False(result.Resolved);
        Assert.True(result.CanEscalate);
        Assert.Equal(DisputeAutoCheckMessages.DeliveryNotSent, result.MessageKey);
    }

    /// <summary>
    /// No rendering in any supported locale may still promise the buyer a trade
    /// offer to accept: the platform does not create one (02 §2.1).
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    [InlineData("es")]
    [InlineData("zh")]
    public void DeliveryMessages_DoNotReferenceAPlatformTradeOffer(string locale)
    {
        foreach (var key in new[]
                 {
                     DisputeAutoCheckMessages.DeliveryAssetGoneNotArrived,
                     DisputeAutoCheckMessages.DeliveryNotSent,
                 })
        {
            var text = DisputeAutoCheckMessages.Localize(key, locale);

            Assert.DoesNotContain("trade offer", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("oferta de intercambio", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("交易报价", text, StringComparison.Ordinal);
        }
    }
}
