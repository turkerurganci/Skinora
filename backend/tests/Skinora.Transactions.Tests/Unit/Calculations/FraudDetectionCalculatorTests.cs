using Skinora.Transactions.Domain.Calculations;

namespace Skinora.Transactions.Tests.Unit.Calculations;

/// <summary>
/// Boundary-value coverage for <see cref="FraudDetectionCalculator"/> per the
/// T55 scenarios — sapma hesaplama, hacim kontrolü, dormant hesap anomali
/// (02 §14.4, 03 §7.1–§7.2). Each branch carries a literal expectation so a
/// future refactor that flips the threshold inequality fails loudly.
/// </summary>
public class FraudDetectionCalculatorTests
{
    // ===================================================================
    // Price deviation (02 §14.4 — |quoted - market| / market)
    // ===================================================================

    [Fact]
    public void PriceDeviation_QuoteAtMarket_IsZero()
    {
        var ratio = FraudDetectionCalculator.CalculatePriceDeviation(100m, 100m);
        Assert.Equal(0m, ratio);
    }

    [Fact]
    public void PriceDeviation_QuoteDoubleMarket_IsOne()
    {
        // quoted 200, market 100 → |200-100|/100 = 1.00 (100%).
        var ratio = FraudDetectionCalculator.CalculatePriceDeviation(200m, 100m);
        Assert.Equal(1m, ratio);
    }

    [Fact]
    public void PriceDeviation_QuoteHalfMarket_IsHalf()
    {
        // quoted 50, market 100 → |50-100|/100 = 0.50.
        var ratio = FraudDetectionCalculator.CalculatePriceDeviation(50m, 100m);
        Assert.Equal(0.5m, ratio);
    }

    [Fact]
    public void PriceDeviation_NullMarketPrice_ReturnsNull()
    {
        var ratio = FraudDetectionCalculator.CalculatePriceDeviation(100m, null);
        Assert.Null(ratio);
    }

    [Fact]
    public void PriceDeviation_ZeroMarketPrice_ReturnsNull()
    {
        // Division-by-zero guard — 02 §14.4 only flags when an actual market
        // signal exists; a 0 price is treated as "no comparable price".
        var ratio = FraudDetectionCalculator.CalculatePriceDeviation(100m, 0m);
        Assert.Null(ratio);
    }

    [Fact]
    public void PriceDeviation_NegativeMarketPrice_ReturnsNull()
    {
        // Defensive — negative prices should never reach here, but the
        // guard keeps the helper total over its decimal? input.
        var ratio = FraudDetectionCalculator.CalculatePriceDeviation(100m, -50m);
        Assert.Null(ratio);
    }

    [Theory]
    [InlineData(120, 100, 0.20)]   // 20% above
    [InlineData(80, 100, 0.20)]    // 20% below — Math.Abs symmetry
    [InlineData(100.5, 100, 0.005)]
    [InlineData(0, 100, 1.0)]      // free item against 100 market
    public void PriceDeviation_KnownPairs_MatchSpec(decimal quoted, decimal market, decimal expected)
    {
        var ratio = FraudDetectionCalculator.CalculatePriceDeviation(quoted, market);
        Assert.Equal(expected, ratio);
    }

    // ===================================================================
    // High-volume detection (02 §14.4 — count or amount, strictly above)
    // ===================================================================

    [Fact]
    public void HighVolume_BothThresholdsNull_ReturnsFalse()
    {
        // Rule completely disabled — neither side configured.
        var trips = FraudDetectionCalculator.IsHighVolume(
            recentTransactionCount: 999,
            recentTotalAmount: 999_999m,
            countThreshold: null,
            amountThreshold: null);

        Assert.False(trips);
    }

    [Fact]
    public void HighVolume_CountAboveThreshold_Trips()
    {
        var trips = FraudDetectionCalculator.IsHighVolume(
            recentTransactionCount: 6,
            recentTotalAmount: 100m,
            countThreshold: 5,
            amountThreshold: null);

        Assert.True(trips);
    }

    [Fact]
    public void HighVolume_CountAtThreshold_DoesNotTrip()
    {
        // Strict greater-than: a seller hitting exactly the limit is OK,
        // only the first overshoot trips. This matches the existing limits
        // semantic (max_concurrent_transactions, etc.).
        var trips = FraudDetectionCalculator.IsHighVolume(
            recentTransactionCount: 5,
            recentTotalAmount: 100m,
            countThreshold: 5,
            amountThreshold: null);

        Assert.False(trips);
    }

    [Fact]
    public void HighVolume_AmountAboveThreshold_Trips()
    {
        var trips = FraudDetectionCalculator.IsHighVolume(
            recentTransactionCount: 1,
            recentTotalAmount: 10_000.01m,
            countThreshold: null,
            amountThreshold: 10_000m);

        Assert.True(trips);
    }

    [Fact]
    public void HighVolume_AmountAtThreshold_DoesNotTrip()
    {
        var trips = FraudDetectionCalculator.IsHighVolume(
            recentTransactionCount: 1,
            recentTotalAmount: 10_000m,
            countThreshold: null,
            amountThreshold: 10_000m);

        Assert.False(trips);
    }

    [Fact]
    public void HighVolume_OnlyCountTrips_StillFlags()
    {
        // Either side trips → flag (logical OR per 02 §14.4 "veya" semantic).
        var trips = FraudDetectionCalculator.IsHighVolume(
            recentTransactionCount: 100,
            recentTotalAmount: 1m,
            countThreshold: 10,
            amountThreshold: 10_000m);

        Assert.True(trips);
    }

    [Fact]
    public void HighVolume_OnlyAmountTrips_StillFlags()
    {
        var trips = FraudDetectionCalculator.IsHighVolume(
            recentTransactionCount: 1,
            recentTotalAmount: 50_000m,
            countThreshold: 10,
            amountThreshold: 10_000m);

        Assert.True(trips);
    }

    [Fact]
    public void HighVolume_ZeroCountThreshold_TreatedAsDisabled()
    {
        // Defensive: 0 or negative thresholds disable the branch — admin
        // entry mistake should not flag every transaction.
        var trips = FraudDetectionCalculator.IsHighVolume(
            recentTransactionCount: 100,
            recentTotalAmount: 1m,
            countThreshold: 0,
            amountThreshold: null);

        Assert.False(trips);
    }

    [Fact]
    public void HighVolume_ZeroAmountThreshold_TreatedAsDisabled()
    {
        var trips = FraudDetectionCalculator.IsHighVolume(
            recentTransactionCount: 1,
            recentTotalAmount: 1m,
            countThreshold: null,
            amountThreshold: 0m);

        Assert.False(trips);
    }

    // ===================================================================
    // Dormant-account anomaly (02 §14.3 — never traded + old account + high attempt)
    // ===================================================================

    [Fact]
    public void DormantAnomaly_AllConditionsMet_Trips()
    {
        var trips = FraudDetectionCalculator.IsDormantAnomaly(
            completedTransactionCount: 0,
            accountAgeDays: 90,
            attemptedAmount: 5_000m,
            dormantMinAgeDays: 30,
            valueThreshold: 1_000m);

        Assert.True(trips);
    }

    [Fact]
    public void DormantAnomaly_HasCompletedTransactions_DoesNotTrip()
    {
        // Any prior trade → not dormant. Heavy user with high volume is a
        // separate signal handled by HIGH_VOLUME, not this rule.
        var trips = FraudDetectionCalculator.IsDormantAnomaly(
            completedTransactionCount: 1,
            accountAgeDays: 365,
            attemptedAmount: 100_000m,
            dormantMinAgeDays: 30,
            valueThreshold: 1_000m);

        Assert.False(trips);
    }

    [Fact]
    public void DormantAnomaly_NewAccount_DoesNotTrip()
    {
        // accountAgeDays < dormantMinAgeDays → caught by T39 new-account
        // limits instead. Dormant rule explicitly excludes this case so the
        // signals don't double-flag.
        var trips = FraudDetectionCalculator.IsDormantAnomaly(
            completedTransactionCount: 0,
            accountAgeDays: 5,
            attemptedAmount: 5_000m,
            dormantMinAgeDays: 30,
            valueThreshold: 1_000m);

        Assert.False(trips);
    }

    [Fact]
    public void DormantAnomaly_AmountAtThreshold_DoesNotTrip()
    {
        // Strict greater-than mirrors HighVolume — only the overshoot trips.
        var trips = FraudDetectionCalculator.IsDormantAnomaly(
            completedTransactionCount: 0,
            accountAgeDays: 90,
            attemptedAmount: 1_000m,
            dormantMinAgeDays: 30,
            valueThreshold: 1_000m);

        Assert.False(trips);
    }

    [Fact]
    public void DormantAnomaly_AmountJustAboveThreshold_Trips()
    {
        var trips = FraudDetectionCalculator.IsDormantAnomaly(
            completedTransactionCount: 0,
            accountAgeDays: 30,
            attemptedAmount: 1_000.01m,
            dormantMinAgeDays: 30,
            valueThreshold: 1_000m);

        Assert.True(trips);
    }

    [Fact]
    public void DormantAnomaly_AccountAgeAtMinimumThreshold_Trips()
    {
        // accountAgeDays >= dormantMinAgeDays — equality counts as dormant.
        var trips = FraudDetectionCalculator.IsDormantAnomaly(
            completedTransactionCount: 0,
            accountAgeDays: 30,
            attemptedAmount: 5_000m,
            dormantMinAgeDays: 30,
            valueThreshold: 1_000m);

        Assert.True(trips);
    }

    [Fact]
    public void DormantAnomaly_ZeroValueThreshold_DisablesRule()
    {
        // Admin entry mistake or unconfigured setting — rule is off.
        var trips = FraudDetectionCalculator.IsDormantAnomaly(
            completedTransactionCount: 0,
            accountAgeDays: 365,
            attemptedAmount: 100_000m,
            dormantMinAgeDays: 30,
            valueThreshold: 0m);

        Assert.False(trips);
    }

    [Fact]
    public void DormantAnomaly_NegativeValueThreshold_DisablesRule()
    {
        var trips = FraudDetectionCalculator.IsDormantAnomaly(
            completedTransactionCount: 0,
            accountAgeDays: 365,
            attemptedAmount: 100_000m,
            dormantMinAgeDays: 30,
            valueThreshold: -1m);

        Assert.False(trips);
    }
}
