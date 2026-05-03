using Skinora.Transactions.Domain.Calculations;

namespace Skinora.Transactions.Tests.Unit.Calculations;

/// <summary>
/// Boundary-value coverage for <see cref="FinancialCalculator"/> per the
/// scenarios mandated in 09 §14.5: normal, boundary, gas fee edge case,
/// overpayment, partial payment refund, precision, and exact-match. The
/// goal is that every branch documented in 02 §5, §4.6-§4.7, 06 §8.3 and
/// 09 §14.4 has a regression test wired to a literal expectation, so a
/// future "tiny refactor" that drifts a digit fails loudly.
/// </summary>
public class FinancialCalculatorTests
{
    // ===================================================================
    // Commission (02 §5, 06 §8.3 — ROUND(price × rate, 6, ToZero))
    // ===================================================================

    [Fact]
    public void Commission_NormalCase_RoundsToZeroAtScaleSix()
    {
        // 100.00 USDT × 2% = 2.000000 — clean case used in 06 §8.3 example.
        var commission = FinancialCalculator.CalculateCommission(100m, 0.02m);

        Assert.Equal(2m, commission);
    }

    [Fact]
    public void Commission_TruncatesTowardZero_NotBankersRound()
    {
        // 33.333333 × 0.02 = 0.66666666 → ToZero truncates to 0.666666.
        // Banker's rounding would yield 0.666667 — the assertion guards
        // against MidpointRounding default drift.
        var commission = FinancialCalculator.CalculateCommission(33.333333m, 0.02m);

        Assert.Equal(0.666666m, commission);
    }

    [Fact]
    public void Commission_AtPrecisionBoundary_KeepsSixDecimals()
    {
        // 0.999999 × 0.02 = 0.01999998 → ToZero → 0.019999.
        var commission = FinancialCalculator.CalculateCommission(0.999999m, 0.02m);

        Assert.Equal(0.019999m, commission);
    }

    [Theory]
    [InlineData(10, 0.02, 0.2)]
    [InlineData(50000, 0.02, 1000)]
    [InlineData(123.45, 0.02, 2.469)]
    [InlineData(99.99, 0.025, 2.49975)]
    public void Commission_KnownPairs_MatchSpec(decimal price, decimal rate, decimal expected)
    {
        var commission = FinancialCalculator.CalculateCommission(price, rate);

        Assert.Equal(expected, commission);
    }

    [Fact]
    public void Commission_ZeroRate_ReturnsZero()
    {
        Assert.Equal(0m, FinancialCalculator.CalculateCommission(100m, 0m));
    }

    [Fact]
    public void Commission_NegativePrice_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FinancialCalculator.CalculateCommission(-1m, 0.02m));
    }

    [Fact]
    public void Commission_NegativeRate_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FinancialCalculator.CalculateCommission(100m, -0.01m));
    }

    // ===================================================================
    // Total (06 §8.3 — price + commissionAmount, no rounding)
    // ===================================================================

    [Fact]
    public void Total_AddsCommissionToPrice_WithoutRerounding()
    {
        var total = FinancialCalculator.CalculateTotal(100m, 2m);

        Assert.Equal(102m, total);
    }

    [Fact]
    public void Total_HandlesSixDecimalCommission()
    {
        var total = FinancialCalculator.CalculateTotal(33.333333m, 0.666666m);

        Assert.Equal(33.999999m, total);
    }

    [Fact]
    public void Total_NegativePrice_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FinancialCalculator.CalculateTotal(-1m, 1m));
    }

    [Fact]
    public void Total_NegativeCommission_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FinancialCalculator.CalculateTotal(1m, -1m));
    }

    // ===================================================================
    // Refund (02 §4.6, 09 §14.4 — totalPaid - gasFee)
    // ===================================================================

    [Fact]
    public void Refund_FullCancellation_DeductsGasFee()
    {
        // Buyer paid 102 (100 price + 2 commission); 1 USDT gas fee.
        // Net refund: 101 USDT.
        var refund = FinancialCalculator.CalculateRefund(102m, 1m);

        Assert.Equal(101m, refund);
    }

    [Fact]
    public void Refund_GasFeeZero_RefundsTotalPaid()
    {
        Assert.Equal(102m, FinancialCalculator.CalculateRefund(102m, 0m));
    }

    [Fact]
    public void Refund_GasFeeExceedsTotal_ReturnsNegative()
    {
        // Caller layer must invoke IsRefundAboveMinimum to gate the
        // outgoing transfer; the calculator does not clamp.
        var refund = FinancialCalculator.CalculateRefund(1m, 5m);

        Assert.Equal(-4m, refund);
    }

    // ===================================================================
    // Minimum refund threshold (09 §14.4 — gasFee × ratio, default 2)
    // ===================================================================

    [Fact]
    public void MinimumRefundThreshold_DefaultRatio_IsTwoTimesGasFee()
    {
        var threshold = FinancialCalculator.CalculateMinimumRefundThreshold(
            gasFee: 1.5m,
            ratio: FinancialCalculator.DefaultMinimumRefundThresholdRatio);

        Assert.Equal(3m, threshold);
    }

    [Fact]
    public void IsRefundAboveMinimum_RefundEqualsThreshold_ReturnsTrue()
    {
        // refund = totalPaid - gasFee = 3 - 1 = 2; threshold = 2 (gasFee × 2).
        // Equality clears the gate (≥ semantic per 09 §14.4 "değilse").
        var passes = FinancialCalculator.IsRefundAboveMinimum(
            totalPaid: 3m, gasFee: 1m, ratio: 2m);

        Assert.True(passes);
    }

    [Fact]
    public void IsRefundAboveMinimum_RefundOneMicroUnitBelowThreshold_ReturnsFalse()
    {
        // refund = 2.999999 - 1 = 1.999999; threshold = 2 → admin alert path.
        var passes = FinancialCalculator.IsRefundAboveMinimum(
            totalPaid: 2.999999m, gasFee: 1m, ratio: 2m);

        Assert.False(passes);
    }

    [Fact]
    public void IsRefundAboveMinimum_NegativeRefund_ReturnsFalse()
    {
        // Gas fee dwarfs the paid amount — refund would be negative,
        // never above threshold.
        var passes = FinancialCalculator.IsRefundAboveMinimum(
            totalPaid: 0.5m, gasFee: 1m, ratio: 2m);

        Assert.False(passes);
    }

    // ===================================================================
    // Seller payout (02 §4.7, 09 §14.4 — gas fee protection)
    // ===================================================================

    [Fact]
    public void SellerPayout_GasFeeBelowThreshold_PlatformAbsorbs()
    {
        // commission = 2; threshold = 2 × 0.10 = 0.20; gasFee = 0.15 ≤ 0.20.
        // Seller receives the full price; platform eats the gas fee.
        var payout = FinancialCalculator.CalculateSellerPayout(
            price: 100m, commissionAmount: 2m, gasFee: 0.15m, gasFeeProtectionRatio: 0.10m);

        Assert.Equal(100m, payout);
    }

    [Fact]
    public void SellerPayout_GasFeeAtThreshold_PlatformAbsorbs()
    {
        // gasFee = threshold = 0.20 → boundary case, "≤" still absorbs.
        var payout = FinancialCalculator.CalculateSellerPayout(
            price: 100m, commissionAmount: 2m, gasFee: 0.20m, gasFeeProtectionRatio: 0.10m);

        Assert.Equal(100m, payout);
    }

    [Fact]
    public void SellerPayout_GasFeeOverThreshold_DeductsExcessFromSeller()
    {
        // commission = 2; threshold = 0.20; gasFee = 0.50.
        // overage = 0.50 - 0.20 = 0.30; payout = 100 - 0.30 = 99.70.
        var payout = FinancialCalculator.CalculateSellerPayout(
            price: 100m, commissionAmount: 2m, gasFee: 0.50m, gasFeeProtectionRatio: 0.10m);

        Assert.Equal(99.70m, payout);
    }

    [Fact]
    public void SellerPayout_GasFeeMassivelyAboveThreshold_PayoutCanGoNegative()
    {
        // Pathological case — caller layer should refuse to send a
        // negative-payout transaction; the calculator just reports the math.
        var payout = FinancialCalculator.CalculateSellerPayout(
            price: 10m, commissionAmount: 0.20m, gasFee: 50m, gasFeeProtectionRatio: 0.10m);

        // threshold = 0.02; overage = 49.98; payout = 10 - 49.98 = -39.98.
        Assert.Equal(-39.98m, payout);
    }

    [Fact]
    public void SellerPayout_GasFeeZero_AlwaysAbsorbed()
    {
        var payout = FinancialCalculator.CalculateSellerPayout(
            price: 100m, commissionAmount: 2m, gasFee: 0m, gasFeeProtectionRatio: 0.10m);

        Assert.Equal(100m, payout);
    }

    // ===================================================================
    // Overpayment (09 §14.4)
    // ===================================================================

    [Fact]
    public void Overpayment_PositiveDelta_ReturnsDifference()
    {
        Assert.Equal(0.50m, FinancialCalculator.CalculateOverpayment(expected: 100m, received: 100.50m));
    }

    [Fact]
    public void Overpayment_ExactMatch_ReturnsZero()
    {
        Assert.Equal(0m, FinancialCalculator.CalculateOverpayment(expected: 100m, received: 100m));
    }

    [Fact]
    public void Overpayment_Underpayment_ReturnsZero()
    {
        // Underpayment is a separate flow (02 §4.4) handled by callers.
        // The overpayment helper clamps to non-negative.
        Assert.Equal(0m, FinancialCalculator.CalculateOverpayment(expected: 100m, received: 80m));
    }

    [Fact]
    public void OverpaymentRefund_DeductsGasFee()
    {
        // Buyer overpaid by 5; gas fee 1 → 4 USDT refundable.
        Assert.Equal(4m, FinancialCalculator.CalculateOverpaymentRefund(overpaymentAmount: 5m, gasFee: 1m));
    }

    [Fact]
    public void OverpaymentRefund_BelowMinimumThreshold_GoesNegative()
    {
        // Tiny overpayment (0.50) with 1 USDT gas fee → -0.50.
        // Caller layer routes to admin alert via IsRefundAboveMinimum.
        Assert.Equal(-0.50m, FinancialCalculator.CalculateOverpaymentRefund(overpaymentAmount: 0.50m, gasFee: 1m));
    }

    // ===================================================================
    // Payment exact match (02 §5, 06 §8.3 — no tolerance)
    // ===================================================================

    [Fact]
    public void IsPaymentExact_EqualAmounts_ReturnsTrue()
    {
        Assert.True(FinancialCalculator.IsPaymentExact(102m, 102m));
    }

    [Fact]
    public void IsPaymentExact_OneMicroUnitDelta_ReturnsFalse()
    {
        // 0.000001 USDT delta = NOT exact. The contract is intentionally
        // strict because payment validation tolerance was rejected
        // explicitly in 02 §5 and 06 §8.3.
        Assert.False(FinancialCalculator.IsPaymentExact(102m, 102.000001m));
    }

    [Fact]
    public void IsPaymentExact_OverpaymentByOneCent_ReturnsFalse()
    {
        Assert.False(FinancialCalculator.IsPaymentExact(102m, 102.01m));
    }

    [Fact]
    public void IsPaymentExact_UnderpaymentByOneCent_ReturnsFalse()
    {
        Assert.False(FinancialCalculator.IsPaymentExact(102m, 101.99m));
    }

    // ===================================================================
    // Round-trip — creation flow integrates Commission + Total exactly
    // the way TransactionCreationService persists the snapshot.
    // ===================================================================

    [Fact]
    public void CreationFlow_RoundTrip_CommissionAndTotalLineUp()
    {
        // Mirrors TransactionCreationService.CreateAsync stage 7.
        const decimal price = 250.75m;
        const decimal rate = 0.02m;

        var commission = FinancialCalculator.CalculateCommission(price, rate);
        var total = FinancialCalculator.CalculateTotal(price, commission);

        Assert.Equal(5.015m, commission);
        Assert.Equal(255.765m, total);
    }
}
