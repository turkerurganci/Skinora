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
    public void For_MAINTENANCE_Returns_All_Six_Active_States()
    {
        var statuses = TimeoutFreezeReasonScopes.For(TimeoutFreezeReason.MAINTENANCE);

        // v3.0 — the two trade-offer states are gone, so the active set is six.
        var expected = new[]
        {
            TransactionStatus.CREATED,
            TransactionStatus.ACCEPTED,
            TransactionStatus.SELLER_CONFIRMED,
            TransactionStatus.PAYMENT_RECEIVED,
            TransactionStatus.ITEM_DELIVERED,
            TransactionStatus.FLAGGED,
        };
        Assert.Equal(expected, statuses);
    }

    [Fact]
    public void For_STEAM_OUTAGE_Returns_Two_Steam_Bound_States()
    {
        // The parties can still trade during a Steam outage; what breaks is the
        // platform's ability to verify it. So the states that freeze are the two
        // whose deadlines depend on a Steam-side observation (02 §23, 03 §11.2).
        var statuses = TimeoutFreezeReasonScopes.For(TimeoutFreezeReason.STEAM_OUTAGE);

        Assert.Equal(
            new[] { TransactionStatus.ACCEPTED, TransactionStatus.PAYMENT_RECEIVED },
            statuses);
    }

    [Fact]
    public void For_BLOCKCHAIN_DEGRADATION_Returns_Only_SELLER_CONFIRMED()
    {
        var statuses = TimeoutFreezeReasonScopes.For(TimeoutFreezeReason.BLOCKCHAIN_DEGRADATION);

        Assert.Equal(new[] { TransactionStatus.SELLER_CONFIRMED }, statuses);
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
