using Skinora.Transactions.Application.Settlement;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Tests.Unit.Settlement;

/// <summary>
/// T129 — the single writer of <c>PayoutEligibleAt</c> (02 §4.5.1, 06 §3.5).
/// </summary>
[Trait("Category", "Unit")]
public sealed class SettlementWindowStamperTests
{
    private static readonly DateTime DeliveredAt = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Stamp_SetsWindowFromDeliveryInstant()
    {
        var transaction = new Transaction();

        SettlementWindowStamper.Stamp(transaction, DeliveredAt, settlementDays: 8);

        Assert.Equal(DeliveredAt.AddDays(8), transaction.PayoutEligibleAt);
    }

    [Fact]
    public void Stamp_HonoursAnAdminConfiguredWindow()
    {
        var transaction = new Transaction();

        SettlementWindowStamper.Stamp(transaction, DeliveredAt, settlementDays: 14);

        Assert.Equal(DeliveredAt.AddDays(14), transaction.PayoutEligibleAt);
    }

    [Fact]
    public void Stamp_IsIdempotent_ANewRoundCannotPushTheDateOut()
    {
        // A retried delivery round must not extend a seller's wait. The column
        // is the seller's payout date, so re-stamping it later is not a harmless
        // recompute — it is money held longer than the rules say.
        var transaction = new Transaction();
        SettlementWindowStamper.Stamp(transaction, DeliveredAt, settlementDays: 8);

        SettlementWindowStamper.Stamp(transaction, DeliveredAt.AddDays(3), settlementDays: 8);

        Assert.Equal(DeliveredAt.AddDays(8), transaction.PayoutEligibleAt);
    }
}
