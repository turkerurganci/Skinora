using Skinora.Shared.Enums;
using Skinora.Transactions.Domain.Calculations;

namespace Skinora.Transactions.Tests.Unit.Calculations;

/// <summary>
/// T149 — the network fee policy's money arithmetic (11 §F8 K4/K8, 02 §4.6,
/// §4.7, §5, 06 §8.3, 08 §3.1a). Expected values are worked out by hand and
/// written as literals; the 02 §5 / 11 §F8 K8 example table is asserted line
/// by line so the documents and the code cannot drift apart.
/// </summary>
public class NetworkFeePolicyCalculatorTests
{
    private static decimal D(string value) =>
        decimal.Parse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture);

    // ─── RoundUpToCents ─────────────────────────────────────────────

    [Theory]
    [InlineData("2.90", "2.90")]          // already whole-cent: unchanged
    [InlineData("2.900001", "2.91")]      // one micro-unit over: next cent
    [InlineData("2.8901", "2.90")]
    [InlineData("2.899999", "2.90")]
    [InlineData("0", "0")]
    [InlineData("0.000001", "0.01")]
    [InlineData("10.005", "10.01")]       // midpoint goes up, not to even
    public void RoundUpToCents_IsCeilingAtTwoDecimals(string input, string expected)
    {
        Assert.Equal(D(expected), FinancialCalculator.RoundUpToCents(D(input)));
    }

    // ─── CalculateTronNetworkFeeLine ────────────────────────────────

    [Fact]
    public void FeeLine_Doc_Example_Is_2_90()
    {
        // 02 §5 / 11 §F8 K8: 1.1 activation + 7.07135 burn top-up (6.4285 +
        // 10%) + 0.27 bandwidth = 8.44135 TRX × 0.34 = 2.870059 × 1.007 =
        // 2.890149… → rounded UP → 2.90.
        Assert.Equal(2.90m, FinancialCalculator.CalculateTronNetworkFeeLine(8_441_350, 0.34m, 0.007m));
    }

    [Fact]
    public void FeeLine_Doc_Figure_8_47_Trx_Is_Also_2_90()
    {
        // The document's rounder 8.47 TRX ≈ 2.88 $: 8.47 × 0.34 × 1.007 = 2.89996 → 2.90.
        Assert.Equal(2.90m, FinancialCalculator.CalculateTronNetworkFeeLine(8_470_000, 0.34m, 0.007m));
    }

    [Fact]
    public void FeeLine_Rounds_Once_At_The_End()
    {
        // Rounding the cost first (2.870059 → 2.88) and then buffering
        // (2.88 × 1.007 = 2.90016 → 2.91) would charge a cent more.
        Assert.Equal(2.90m, FinancialCalculator.CalculateTronNetworkFeeLine(8_441_349, 0.34m, 0.007m));
    }

    [Fact]
    public void FeeLine_Exact_Cent_Stays_Exact()
    {
        // 10 TRX × 0.25 = 2.50 exactly, no buffer → 2.50, not 2.51.
        Assert.Equal(2.50m, FinancialCalculator.CalculateTronNetworkFeeLine(10_000_000, 0.25m, 0m));
    }

    [Fact]
    public void FeeLine_One_Sun_Over_An_Exact_Cent_Goes_Up()
    {
        // 10.000001 TRX × 0.25 = 2.50000025 → 2.51.
        Assert.Equal(2.51m, FinancialCalculator.CalculateTronNetworkFeeLine(10_000_001, 0.25m, 0m));
    }

    [Fact]
    public void FeeLine_Buffer_Is_Applied()
    {
        // 10 TRX × 0.25 = 2.50 × 1.10 = 2.75.
        Assert.Equal(2.75m, FinancialCalculator.CalculateTronNetworkFeeLine(10_000_000, 0.25m, 0.10m));
    }

    [Fact]
    public void FeeLine_Zero_Cost_Is_Zero()
    {
        Assert.Equal(0m, FinancialCalculator.CalculateTronNetworkFeeLine(0, 0.34m, 0.007m));
    }

    [Fact]
    public void FeeLine_Is_Always_Whole_Cent()
    {
        for (var sun = 8_000_000L; sun < 8_000_000L + 5_000; sun += 37)
        {
            var line = FinancialCalculator.CalculateTronNetworkFeeLine(sun, 0.3417m, 0.007m);
            Assert.True(FinancialCalculator.IsWholeCents(line), $"{sun} sun → {line}");
            // Never below the cost it covers.
            Assert.True(line >= sun / 1_000_000m * 0.3417m * 1.007m);
        }
    }

    [Fact]
    public void FeeLine_Rejects_Bad_Inputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FinancialCalculator.CalculateTronNetworkFeeLine(-1, 0.34m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinancialCalculator.CalculateTronNetworkFeeLine(1, 0m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinancialCalculator.CalculateTronNetworkFeeLine(1, 0.34m, -0.01m));
    }

    // ─── Totals (02 §5 table) ───────────────────────────────────────

    [Theory]
    [InlineData("20.00", "2.90", "23.30")]     // TRON 20 $
    [InlineData("20.00", "0", "20.40")]        // Ethereum 20 $
    [InlineData("200.00", "2.90", "206.90")]   // TRON 200 $
    [InlineData("200.00", "0", "204.00")]      // Ethereum 200 $
    public void Total_Doc_Table(string price, string fee, string expectedTotal)
    {
        var p = D(price);
        var commission = FinancialCalculator.CalculateCommission(p, 0.02m);
        Assert.Equal(D(expectedTotal), FinancialCalculator.CalculateTotal(p, commission, D(fee)));
    }

    [Fact]
    public void Total_Refuses_A_Fee_Line_That_Is_Not_Whole_Cent()
    {
        Assert.Throws<ArgumentException>(() => FinancialCalculator.CalculateTotal(20m, 0.40m, 2.899m));
    }

    // ─── Seller payout per network (02 §4.7, 02 §5 table) ───────────

    [Theory]
    // TRON: 20 − (2.29 − 0.04) = 17.75 and 20 − (4.53 − 0.04) = 15.51.
    [InlineData("20.00", "2.29", "17.75")]
    [InlineData("20.00", "4.53", "15.51")]
    // TRON: 200 − (2.29 − 0.40) = 198.11 and 200 − (4.53 − 0.40) = 195.87.
    [InlineData("200.00", "2.29", "198.11")]
    [InlineData("200.00", "4.53", "195.87")]
    public void TronPayout_Doc_Table(string price, string payoutFee, string expected)
    {
        var p = D(price);
        var commission = FinancialCalculator.CalculateCommission(p, 0.02m);
        Assert.True(NetworkFeeRules.SellerBearsPayoutFeeOverage(BlockchainNetwork.TRON));
        Assert.Equal(D(expected),
            FinancialCalculator.CalculateSellerPayout(p, commission, D(payoutFee), 0.10m));
    }

    [Fact]
    public void Network_Rules_Tron_Buyer_Line_And_Seller_Overage_Ethereum_Neither()
    {
        Assert.True(NetworkFeeRules.BuyerPaysNetworkFeeLine(BlockchainNetwork.TRON));
        Assert.True(NetworkFeeRules.SellerBearsPayoutFeeOverage(BlockchainNetwork.TRON));
        Assert.False(NetworkFeeRules.BuyerPaysNetworkFeeLine(BlockchainNetwork.ETHEREUM));
        Assert.False(NetworkFeeRules.SellerBearsPayoutFeeOverage(BlockchainNetwork.ETHEREUM));
    }

    // ─── Refundable total (02 §4.6, 08 §3.1a) ───────────────────────

    [Fact]
    public void Refundable_Before_Sweep_Returns_The_Fee_Line_In_Full()
    {
        // 23.30 = 20 + 0.40 + 2.90; nothing swept → all of it.
        Assert.Equal(23.30m, FinancialCalculator.CalculateRefundableTotal(23.30m, 2.90m, sweepHasRun: false));
    }

    [Fact]
    public void Refundable_After_Sweep_Keeps_The_Fee_Line()
    {
        // The line paid for the sweep that ran: price + commission only.
        Assert.Equal(20.40m, FinancialCalculator.CalculateRefundableTotal(23.30m, 2.90m, sweepHasRun: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Refundable_Without_A_Line_Is_The_Total(bool swept)
    {
        // Ethereum (0) and pre-T149 / unaccepted rows (null).
        Assert.Equal(20.40m, FinancialCalculator.CalculateRefundableTotal(20.40m, 0m, swept));
        Assert.Equal(20.40m, FinancialCalculator.CalculateRefundableTotal(20.40m, null, swept));
    }

    [Fact]
    public void Refundable_Rejects_A_Line_Above_The_Total()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FinancialCalculator.CalculateRefundableTotal(2m, 2.90m, sweepHasRun: true));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FinancialCalculator.CalculateRefundableTotal(20m, -0.01m, sweepHasRun: false));
    }

    [Fact]
    public void Refund_Before_Sweep_Doc_Example()
    {
        // 02 §4.6: price + commission + line − the refund's own gas.
        var refundable = FinancialCalculator.CalculateRefundableTotal(23.30m, 2.90m, sweepHasRun: false);
        Assert.Equal(21.30m, FinancialCalculator.CalculateRefund(refundable, gasFee: 2.00m));
    }
}
