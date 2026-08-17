using Skinora.Disputes.Application.AutoCheckers;
using Skinora.Transactions.Application.Delivery;
using Skinora.Transactions.Domain.Entities;
using Xunit;

namespace Skinora.Disputes.Tests.Unit;

/// <summary>
/// T130 — the delivery auto-checker maps a <see cref="DeliveryDisputeOutcome"/>
/// onto the 03 §6.2 answer the buyer reads. These tests pin the outcome, the
/// escalation route AND the message key, because the message is what the buyer
/// is told about their money.
/// </summary>
/// <remarks>
/// The checker no longer reads <c>DeliveryEvidence</c> flags itself — that
/// shortcut is exactly what produced the launch-gate deadlock — so the round is
/// stubbed and each of its five outcomes asserted in turn.
/// </remarks>
[Trait("Category", "Unit")]
public class DeliveryDisputeAutoCheckerTests
{
    private static Task<AutoCheckResult> CheckAsync(DeliveryDisputeOutcome outcome) =>
        new DeliveryDisputeAutoChecker(new StubRound(outcome))
            .CheckAsync(new Transaction { Id = Guid.NewGuid() }, CancellationToken.None);

    [Fact]
    public async Task Delivered_ClosesTheDispute()
    {
        var result = await CheckAsync(DeliveryDisputeOutcome.Delivered);

        Assert.True(result.Resolved);
        Assert.False(result.AutoEscalated);
        Assert.Equal(DisputeAutoCheckMessages.DeliveryDelivered, result.MessageKey);
    }

    /// <summary>
    /// 03 §6.2 Sonuç C — "Otomatik olarak admin'e yükseltilir (kullanıcı aksiyonu
    /// beklenmez)". Before T130 this branch left the dispute OPEN and told the
    /// buyer to press escalate themselves, on the one signature that is a
    /// positive finding about a seller.
    /// </summary>
    [Fact]
    public async Task MisdeliverySignature_AutoEscalates()
    {
        var result = await CheckAsync(DeliveryDisputeOutcome.MisdeliverySignature);

        Assert.False(result.Resolved);
        Assert.True(result.AutoEscalated);
        Assert.Equal(DisputeAutoCheckMessages.DeliveryAssetGoneNotArrived, result.MessageKey);
    }

    [Fact]
    public async Task NotSent_StaysOpenAndEscalatable()
    {
        var result = await CheckAsync(DeliveryDisputeOutcome.NotSent);

        Assert.False(result.Resolved);
        Assert.False(result.AutoEscalated);
        Assert.True(result.CanEscalate);
        Assert.Equal(DisputeAutoCheckMessages.DeliveryNotSent, result.MessageKey);
    }

    /// <summary>
    /// <b>The launch-gate deadlock regression</b> (T127 validation finding B5,
    /// T130 acceptance criterion). With the gate closed the platform holds
    /// sufficient inventory evidence without releasing money. The pre-T130
    /// checker answered "delivered" from the flags, which closed the dispute
    /// with <c>CanEscalate = false</c> — the automatic route gated, the manual
    /// route shut, and the buyer's funds with no exit at all.
    /// </summary>
    [Fact]
    public async Task LaunchGateClosed_DoesNotResolveAsDelivered_AndKeepsTheEscalationRoute()
    {
        var result = await CheckAsync(DeliveryDisputeOutcome.PendingReview);

        Assert.False(result.Resolved);
        Assert.False(result.AutoEscalated);
        Assert.True(result.CanEscalate);
        Assert.Equal(DisputeAutoCheckMessages.DeliveryEvidenceUnderReview, result.MessageKey);
    }

    /// <summary>
    /// 03 §6.2 Sonuç D. The pre-T130 checker had no message for this case and
    /// fell through to <c>DELIVERY_NOT_SENT</c> — a negative finding about a
    /// seller the platform had made no observation about (08 §2.3).
    /// </summary>
    [Fact]
    public async Task Unreadable_StaysOpen_AndSaysSoInsteadOfBlamingTheSeller()
    {
        var result = await CheckAsync(DeliveryDisputeOutcome.Unreadable);

        Assert.False(result.Resolved);
        Assert.True(result.CanEscalate);
        Assert.Equal(DisputeAutoCheckMessages.DeliveryInventoryUnreadable, result.MessageKey);
        Assert.NotEqual(DisputeAutoCheckMessages.DeliveryNotSent, result.MessageKey);
    }

    /// <summary>
    /// Every outcome the round can produce must have an explicit arm. A new
    /// verdict silently landing on the default branch would tell the buyer their
    /// inventory was unreadable when it was not.
    /// </summary>
    [Theory]
    [InlineData(DeliveryDisputeOutcome.Delivered)]
    [InlineData(DeliveryDisputeOutcome.NotSent)]
    [InlineData(DeliveryDisputeOutcome.MisdeliverySignature)]
    [InlineData(DeliveryDisputeOutcome.Unreadable)]
    [InlineData(DeliveryDisputeOutcome.PendingReview)]
    public async Task EveryOutcome_ProducesADeliveryMessageKey(DeliveryDisputeOutcome outcome)
    {
        var result = await CheckAsync(outcome);

        Assert.StartsWith("DELIVERY_", result.MessageKey, StringComparison.Ordinal);

        // Exactly one of the three terminal shapes, never two.
        Assert.False(result.Resolved && result.AutoEscalated);
        Assert.False(result.CanSubmitTxHash);
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
                     DisputeAutoCheckMessages.DeliveryInventoryUnreadable,
                     DisputeAutoCheckMessages.DeliveryEvidenceUnderReview,
                 })
        {
            var text = DisputeAutoCheckMessages.Localize(key, locale);

            Assert.DoesNotContain("trade offer", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("oferta de intercambio", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("交易报价", text, StringComparison.Ordinal);
        }
    }

    private sealed class StubRound : IDeliveryDisputeRound
    {
        private readonly DeliveryDisputeOutcome _outcome;

        public StubRound(DeliveryDisputeOutcome outcome) => _outcome = outcome;

        public Task<DeliveryDisputeOutcome> RunAsync(
            Transaction transaction, CancellationToken cancellationToken)
            => Task.FromResult(_outcome);
    }
}
