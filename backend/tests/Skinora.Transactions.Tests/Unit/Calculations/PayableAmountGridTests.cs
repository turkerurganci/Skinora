using System.Globalization;
using Skinora.Shared.Domain;
using Skinora.Transactions.Domain.Calculations;

namespace Skinora.Transactions.Tests.Unit.Calculations;

/// <summary>
/// T144 — the buyer's payable total is always 2-decimal and the amount shown
/// is the amount validation expects (02 §5, 06 §8.3, 09 §7.2).
/// </summary>
/// <remarks>
/// <para>
/// Before T144 the commission was truncated at six decimals, so for prices
/// 10.00–99.99 at 2% only ~2% of totals were whole-cent: the screen showed
/// "10.76" while <c>PaymentAddress.ExpectedAmount</c> held 10.761, and a
/// buyer paying from BtcTurk or Paribu (at most two decimals) could never
/// match it.
/// </para>
/// <para>
/// The expected commission is computed here with integer arithmetic
/// (<c>priceCents × rateUnits / 10 000</c>, integer division), not with the
/// calculator's <see cref="Math.Round(decimal, int, MidpointRounding)"/>, so a
/// change to the production rounding call cannot also move the reference.
/// </para>
/// </remarks>
public class PayableAmountGridTests
{
    /// <summary>
    /// Rates on the <c>decimal(5,4)</c> grid: the default 2%, a value with a
    /// half-cent step, odd basis-point rates, the smallest and a large rate.
    /// </summary>
    public static TheoryData<decimal> Rates => new()
    {
        0.02m, 0.025m, 0.0175m, 0.0333m, 0.0001m, 0.0999m, 0.15m,
    };

    [Theory]
    [MemberData(nameof(Rates))]
    public void EveryPrice_10_00_To_99_99_HasWholeCentTotal_ShownAsExpected(decimal rate)
    {
        var rateUnits = (long)(rate * 10_000m);
        Assert.Equal(rate, rateUnits / 10_000m); // rate really is on the 4-decimal grid

        for (long priceCents = 1_000; priceCents <= 9_999; priceCents++)
        {
            AssertPayable(priceCents, rateUnits, rate);
        }
    }

    [Theory]
    [MemberData(nameof(Rates))]
    public void EdgePrices_HaveWholeCentTotal_ShownAsExpected(decimal rate)
    {
        var rateUnits = (long)(rate * 10_000m);
        long[] edgeCents =
        [
            1, 2, 49, 50, 51, 99, 100, 101, 999,      // below 10.00
            10_000, 10_001, 12_345, 99_999, 100_000,  // 100.00 – 1 000.00
            5_000_000, 5_000_001, 9_999_999_999,      // 50 000.00 and 99 999 999.99
        ];

        foreach (var priceCents in edgeCents)
        {
            AssertPayable(priceCents, rateUnits, rate);
        }
    }

    [Fact]
    public void Display_Of_WholeCentAmounts_Is_The_Two_Decimal_Api_Form()
    {
        // MoneyFormat.Display and the API's "0.00" agree on every whole-cent
        // amount, so the detail screen and the notification text are the same.
        for (long cents = 0; cents <= 20_000; cents++)
        {
            var amount = cents / 100m;
            Assert.Equal(
                amount.ToString("0.00", CultureInfo.InvariantCulture),
                MoneyFormat.Display(amount));
        }
    }

    private static void AssertPayable(long priceCents, long rateUnits, decimal rate)
    {
        var price = priceCents / 100m;

        var commission = FinancialCalculator.CalculateCommission(price, rate);
        var total = FinancialCalculator.CalculateTotal(price, commission);

        // Independent reference: integer cents, truncating division.
        var expectedCommissionCents = priceCents * rateUnits / 10_000;
        var expectedCommission = expectedCommissionCents / 100m;
        Assert.True(
            expectedCommission == commission,
            $"price {price} rate {rate}: commission {commission}, expected {expectedCommission}");

        // Whole-cent commission and total — the payable is 2-decimal.
        Assert.True(FinancialCalculator.IsWholeCents(commission), $"price {price} rate {rate}: commission {commission} not whole-cent");
        Assert.True(FinancialCalculator.IsWholeCents(total), $"price {price} rate {rate}: total {total} not whole-cent");
        Assert.Equal(price + commission, total);

        // Never charges more than price × rate, never under-charges by a cent.
        var exact = price * rate;
        Assert.True(commission <= exact, $"price {price} rate {rate}: commission {commission} > exact {exact}");
        Assert.True(exact - commission < 0.01m, $"price {price} rate {rate}: under-charge {exact - commission} ≥ 0.01");

        // Displayed == expected: the text the buyer reads parses back to the
        // exact value validation compares against, and carries two decimals.
        var shown = MoneyFormat.Display(total);
        Assert.Equal(total, decimal.Parse(shown, NumberStyles.Number, CultureInfo.InvariantCulture));
        Assert.Equal(2, shown.Length - shown.IndexOf('.', StringComparison.Ordinal) - 1);
        Assert.Equal(total.ToString("0.00", CultureInfo.InvariantCulture), shown);
    }
}
