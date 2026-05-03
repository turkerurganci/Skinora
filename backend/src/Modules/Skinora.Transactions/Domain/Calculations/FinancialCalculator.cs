namespace Skinora.Transactions.Domain.Calculations;

/// <summary>
/// Pure financial calculations for the Skinora escrow flow (T52 — 02 §5,
/// §4.6-§4.7, 06 §8.3, 09 §14). All functions are deterministic and side
/// effect free so they can be reused by transaction creation, payout,
/// refund and webhook validation paths without DI plumbing.
/// </summary>
/// <remarks>
/// <para>
/// Every monetary value is <see cref="decimal"/>. <c>float</c> and
/// <c>double</c> are forbidden across the platform (09 §14.1) — silent
/// floating point drift in an escrow flow becomes a real fund loss.
/// </para>
/// <para>
/// Rounding is applied <em>only</em> at the boundary where a value lands
/// in storage or the wire — i.e. <see cref="CalculateCommission"/>, since
/// <c>CommissionAmount</c> persists as <c>decimal(18,6)</c>. Sums and
/// subtractions stay at full <see cref="decimal"/> precision per 09 §14.3.
/// </para>
/// </remarks>
public static class FinancialCalculator
{
    /// <summary>
    /// Storage and computation scale for every monetary field — matches the
    /// <c>decimal(18,6)</c> column type defined in
    /// <c>TransactionConfiguration</c> and 06 §8.3.
    /// </summary>
    public const int MoneyScale = 6;

    /// <summary>
    /// Documented default for <c>gas_fee_protection_ratio</c> (02 §4.7,
    /// 09 §14.4). The seeded SystemSetting carries the same value; this
    /// constant is the formula-side fallback for code paths that do not
    /// resolve the setting (T53 wires the live read).
    /// </summary>
    public const decimal DefaultGasFeeProtectionRatio = 0.10m;

    /// <summary>
    /// Documented default for <c>min_refund_threshold_ratio</c> (09 §14.4).
    /// Refunds whose net amount falls below <c>gasFee × ratio</c> are
    /// suppressed and routed to admin alert.
    /// </summary>
    public const decimal DefaultMinimumRefundThresholdRatio = 2m;

    /// <summary>
    /// Truncating round to <see cref="MoneyScale"/> using
    /// <see cref="MidpointRounding.ToZero"/>. The user-facing rationale
    /// (09 §14.3) is "no rounding works against the user" — banker's
    /// rounding leaks a fraction of a USDT here and there which is
    /// visible in audits and erodes trust.
    /// </summary>
    public static decimal RoundMoney(decimal value) =>
        Math.Round(value, MoneyScale, MidpointRounding.ToZero);

    /// <summary>
    /// <c>commission = ROUND(price × commissionRate, 6, ToZero)</c>
    /// (02 §5, 06 §8.3). The result is the snapshot that lands in
    /// <c>Transaction.CommissionAmount</c>.
    /// </summary>
    public static decimal CalculateCommission(decimal price, decimal commissionRate)
    {
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price), price, "Price must be non-negative.");
        if (commissionRate < 0)
            throw new ArgumentOutOfRangeException(nameof(commissionRate), commissionRate, "Commission rate must be non-negative.");

        return RoundMoney(price * commissionRate);
    }

    /// <summary>
    /// <c>totalAmount = price + commissionAmount</c> — what the buyer
    /// transfers and what <c>PaymentAddress.ExpectedAmount</c> persists
    /// (06 §8.3, 02 §5). Caller must pass an already-rounded
    /// <paramref name="commissionAmount"/>.
    /// </summary>
    public static decimal CalculateTotal(decimal price, decimal commissionAmount)
    {
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price), price, "Price must be non-negative.");
        if (commissionAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(commissionAmount), commissionAmount, "Commission amount must be non-negative.");

        return price + commissionAmount;
    }

    /// <summary>
    /// Refund amount sent to the buyer for a full cancellation
    /// (02 §4.6, 09 §14.4): <c>refund = totalPaid - gasFee</c>. Negative
    /// or sub-threshold results are routed to the admin alert path via
    /// <see cref="IsRefundAboveMinimum"/> — the calculator stays pure.
    /// </summary>
    public static decimal CalculateRefund(decimal totalPaid, decimal gasFee)
    {
        if (totalPaid < 0)
            throw new ArgumentOutOfRangeException(nameof(totalPaid), totalPaid, "Total paid must be non-negative.");
        if (gasFee < 0)
            throw new ArgumentOutOfRangeException(nameof(gasFee), gasFee, "Gas fee must be non-negative.");

        return totalPaid - gasFee;
    }

    /// <summary>
    /// <c>minimumRefundThreshold = gasFee × ratio</c> (default ratio = 2,
    /// 09 §14.4). When a refund falls below this floor the platform
    /// suppresses the outgoing transfer because the gas cost would
    /// dominate; admin alert handles the residue.
    /// </summary>
    public static decimal CalculateMinimumRefundThreshold(decimal gasFee, decimal ratio)
    {
        if (gasFee < 0)
            throw new ArgumentOutOfRangeException(nameof(gasFee), gasFee, "Gas fee must be non-negative.");
        if (ratio < 0)
            throw new ArgumentOutOfRangeException(nameof(ratio), ratio, "Threshold ratio must be non-negative.");

        return gasFee * ratio;
    }

    /// <summary>
    /// True when the refund (<c>totalPaid - gasFee</c>) clears the
    /// minimum threshold (<c>gasFee × ratio</c>). 09 §14.4 spec:
    /// "if refund &lt; minimum: do not refund → admin alert".
    /// </summary>
    public static bool IsRefundAboveMinimum(decimal totalPaid, decimal gasFee, decimal ratio)
    {
        var refund = CalculateRefund(totalPaid, gasFee);
        var threshold = CalculateMinimumRefundThreshold(gasFee, ratio);
        return refund >= threshold;
    }

    /// <summary>
    /// Seller payout with the gas-fee-protection rule (02 §4.7, 09 §14.4):
    /// <list type="bullet">
    ///   <item>If <c>gasFee ≤ commission × protectionRatio</c> →
    ///         <c>payout = price</c> (platform absorbs the gas).</item>
    ///   <item>Else → <c>payout = price - (gasFee - commission × protectionRatio)</c>
    ///         (overage is deducted from the seller).</item>
    /// </list>
    /// The seller is shielded from typical chain congestion but cannot
    /// transfer the unbounded blockchain risk to the platform.
    /// </summary>
    public static decimal CalculateSellerPayout(
        decimal price,
        decimal commissionAmount,
        decimal gasFee,
        decimal gasFeeProtectionRatio)
    {
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price), price, "Price must be non-negative.");
        if (commissionAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(commissionAmount), commissionAmount, "Commission amount must be non-negative.");
        if (gasFee < 0)
            throw new ArgumentOutOfRangeException(nameof(gasFee), gasFee, "Gas fee must be non-negative.");
        if (gasFeeProtectionRatio < 0)
            throw new ArgumentOutOfRangeException(nameof(gasFeeProtectionRatio), gasFeeProtectionRatio, "Protection ratio must be non-negative.");

        var threshold = commissionAmount * gasFeeProtectionRatio;
        if (gasFee <= threshold)
            return price;

        var overage = gasFee - threshold;
        return price - overage;
    }

    /// <summary>
    /// Overpayment amount: <c>received - expected</c> when positive,
    /// otherwise zero. Caller decides what to do with the residue
    /// (refund vs admin alert).
    /// </summary>
    public static decimal CalculateOverpayment(decimal expected, decimal received)
    {
        if (expected < 0)
            throw new ArgumentOutOfRangeException(nameof(expected), expected, "Expected amount must be non-negative.");
        if (received < 0)
            throw new ArgumentOutOfRangeException(nameof(received), received, "Received amount must be non-negative.");

        var diff = received - expected;
        return diff > 0 ? diff : 0m;
    }

    /// <summary>
    /// Net refund payable to the buyer after subtracting the gas fee from
    /// the overpayment (09 §14.4). Caller checks
    /// <see cref="IsRefundAboveMinimum"/>-style threshold logic with
    /// <paramref name="overpaymentAmount"/> as the source of refund.
    /// </summary>
    public static decimal CalculateOverpaymentRefund(decimal overpaymentAmount, decimal gasFee)
    {
        if (overpaymentAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(overpaymentAmount), overpaymentAmount, "Overpayment must be non-negative.");
        if (gasFee < 0)
            throw new ArgumentOutOfRangeException(nameof(gasFee), gasFee, "Gas fee must be non-negative.");

        return overpaymentAmount - gasFee;
    }

    /// <summary>
    /// Strict equality between expected and received payment — 02 §5 and
    /// 06 §8.3 explicitly forbid a tolerance. Decimal equality is exact
    /// at this scale so a "fuzzy" overload would be a footgun.
    /// </summary>
    public static bool IsPaymentExact(decimal expected, decimal received) =>
        expected == received;
}
