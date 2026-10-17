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
/// in storage or the wire — i.e. <see cref="CalculateCommission"/>. Sums and
/// subtractions stay at full <see cref="decimal"/> precision per 09 §14.3.
/// </para>
/// <para>
/// T144 — the amount the buyer is asked to send is a whole-cent value
/// (02 §5, 06 §8.3, 09 §7.2). Turkish exchanges (BtcTurk, Paribu) send USDT
/// with at most two decimals and payment validation is exact, so a payable
/// total with a third digit could never be matched. The price is already
/// whole-cent (07 §7.2), the commission is truncated to the cent
/// (<see cref="PayableScale"/>, <see cref="MidpointRounding.ToZero"/> — never
/// in the platform's favour), and the total is their sum, so it is
/// whole-cent by construction. Storage stays <c>decimal(18,6)</c>.
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
    /// Scale of every amount the buyer is asked to send (T144): price,
    /// commission, the TRON network fee line (T149) and their total. Two
    /// decimals — the most a Turkish exchange withdrawal carries.
    /// </summary>
    public const int PayableScale = 2;

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
    /// True when <paramref name="value"/> has no digits beyond the cent,
    /// i.e. it is a valid payable amount (T144).
    /// </summary>
    public static bool IsWholeCents(decimal value) =>
        decimal.Round(value, PayableScale) == value;

    /// <summary>
    /// <c>commission = ROUND(price × commissionRate, 2, ToZero)</c>
    /// (T144; 02 §5, 06 §8.3, 09 §14.3). The result is the snapshot that
    /// lands in <c>Transaction.CommissionAmount</c>. Truncation means the
    /// buyer never pays more than <c>price × rate</c>; the platform forgoes
    /// less than one cent per transaction.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="price"/> has digits beyond the cent. The create path
    /// already rejects such prices (07 §7.2); this guards the invariant that
    /// the payable total is whole-cent.
    /// </exception>
    public static decimal CalculateCommission(decimal price, decimal commissionRate)
    {
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price), price, "Price must be non-negative.");
        if (commissionRate < 0)
            throw new ArgumentOutOfRangeException(nameof(commissionRate), commissionRate, "Commission rate must be non-negative.");
        EnsureWholeCents(price, nameof(price));

        return Math.Round(price * commissionRate, PayableScale, MidpointRounding.ToZero);
    }

    /// <summary>
    /// <c>totalAmount = price + commissionAmount + networkFeeAmount</c> — what
    /// the buyer transfers and what <c>PaymentAddress.ExpectedAmount</c>
    /// persists (06 §8.3, 02 §5). Every component must already be whole-cent,
    /// so the sum is too (T144). <paramref name="networkFeeAmount"/> is the
    /// TRON network fee line (11 §F8 K8): always 0 until T149 adds the line,
    /// and T149 rounds it up to the cent before it reaches this method.
    /// </summary>
    public static decimal CalculateTotal(decimal price, decimal commissionAmount, decimal networkFeeAmount = 0m)
    {
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price), price, "Price must be non-negative.");
        if (commissionAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(commissionAmount), commissionAmount, "Commission amount must be non-negative.");
        if (networkFeeAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(networkFeeAmount), networkFeeAmount, "Network fee must be non-negative.");
        EnsureWholeCents(price, nameof(price));
        EnsureWholeCents(commissionAmount, nameof(commissionAmount));
        EnsureWholeCents(networkFeeAmount, nameof(networkFeeAmount));

        return price + commissionAmount + networkFeeAmount;
    }

    private static void EnsureWholeCents(decimal value, string paramName)
    {
        if (!IsWholeCents(value))
            throw new ArgumentException(
                $"{paramName} {value} must be a whole-cent amount — the buyer's payable total is 2-decimal (T144).",
                paramName);
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
    /// Reconstructs the 07 §7.5 seller-payout breakdown from stored data for
    /// the read path. The amount the seller actually lost to gas is exact
    /// (<c>price − netAmount</c>); the total gas estimate used at payout time
    /// was snapshotted onto <c>BlockchainTransaction.GasFee</c>, so the
    /// commission-borne share is <c>total − fromSeller</c>. A legacy row with
    /// no snapshot falls back to <c>total = fromSeller</c> (commission share
    /// reported as zero) rather than guessing.
    /// </summary>
    public static SellerPayoutSplit ReconstructSellerPayoutSplit(
        decimal price, decimal netAmount, decimal? gasFeeSnapshot)
    {
        var fromSeller = Math.Max(0m, price - netAmount);
        var total = gasFeeSnapshot is { } g && g >= fromSeller ? g : fromSeller;
        return new SellerPayoutSplit(
            GrossAmount: price,
            NetAmount: netAmount,
            GasFeeFromSeller: fromSeller,
            GasFeeFromCommission: total - fromSeller,
            TotalGasFee: total);
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

/// <summary>
/// Reconstructed seller-payout breakdown (07 §7.5). All values are full
/// <see cref="decimal"/> precision; the read path formats them at the wire
/// boundary. Invariants: <c>NetAmount = GrossAmount − GasFeeFromSeller</c> and
/// <c>TotalGasFee = GasFeeFromCommission + GasFeeFromSeller</c>.
/// </summary>
public readonly record struct SellerPayoutSplit(
    decimal GrossAmount,
    decimal NetAmount,
    decimal GasFeeFromSeller,
    decimal GasFeeFromCommission,
    decimal TotalGasFee);
