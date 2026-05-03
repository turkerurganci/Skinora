using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Timeouts;

namespace Skinora.Transactions.Tests.Unit.Timeouts;

/// <summary>
/// Unit coverage for <see cref="TimeoutFreezeReasonScopes"/> — asserts the
/// reason → status mapping documented in 02 §3.3 and 05 §4.4.
/// </summary>
public class TimeoutFreezeReasonScopesTests
{
    [Fact]
    public void For_MAINTENANCE_Returns_All_Eight_Active_States()
    {
        var statuses = TimeoutFreezeReasonScopes.For(TimeoutFreezeReason.MAINTENANCE);

        var expected = new[]
        {
            TransactionStatus.CREATED,
            TransactionStatus.ACCEPTED,
            TransactionStatus.TRADE_OFFER_SENT_TO_SELLER,
            TransactionStatus.ITEM_ESCROWED,
            TransactionStatus.PAYMENT_RECEIVED,
            TransactionStatus.TRADE_OFFER_SENT_TO_BUYER,
            TransactionStatus.ITEM_DELIVERED,
            TransactionStatus.FLAGGED,
        };
        Assert.Equal(expected, statuses);
    }

    [Fact]
    public void For_STEAM_OUTAGE_Returns_Two_Steam_Bound_States()
    {
        var statuses = TimeoutFreezeReasonScopes.For(TimeoutFreezeReason.STEAM_OUTAGE);

        Assert.Equal(
            new[] { TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, TransactionStatus.TRADE_OFFER_SENT_TO_BUYER },
            statuses);
    }

    [Fact]
    public void For_BLOCKCHAIN_DEGRADATION_Returns_Only_ITEM_ESCROWED()
    {
        var statuses = TimeoutFreezeReasonScopes.For(TimeoutFreezeReason.BLOCKCHAIN_DEGRADATION);

        Assert.Equal(new[] { TransactionStatus.ITEM_ESCROWED }, statuses);
    }

    [Fact]
    public void For_EMERGENCY_HOLD_Throws_ArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => TimeoutFreezeReasonScopes.For(TimeoutFreezeReason.EMERGENCY_HOLD));
        Assert.Contains("EMERGENCY_HOLD", ex.Message);
    }

    [Fact]
    public void Active_States_Exclude_Terminal_States()
    {
        var statuses = TimeoutFreezeReasonScopes.For(TimeoutFreezeReason.MAINTENANCE);

        // Terminal states must never be eligible for a freeze pass.
        Assert.DoesNotContain(TransactionStatus.COMPLETED, statuses);
        Assert.DoesNotContain(TransactionStatus.CANCELLED_TIMEOUT, statuses);
        Assert.DoesNotContain(TransactionStatus.CANCELLED_SELLER, statuses);
        Assert.DoesNotContain(TransactionStatus.CANCELLED_BUYER, statuses);
        Assert.DoesNotContain(TransactionStatus.CANCELLED_ADMIN, statuses);
    }
}
