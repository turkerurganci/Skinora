namespace Skinora.Transactions.Domain.Calculations;

/// <summary>
/// Pure fraud-detection calculations for the pre-create AML pipeline (T55 —
/// 02 §14.4, 03 §7.1–§7.2). Each helper is deterministic and side-effect free
/// so the rule engine can wire them together without dragging the DB context
/// or system clock into unit tests.
/// </summary>
/// <remarks>
/// <para>
/// The calculator owns the <em>arithmetic</em> of each rule; the
/// <c>FraudPreCheckService</c> owns the <em>plumbing</em> (setting reads,
/// transaction aggregation, priority ordering).
/// </para>
/// </remarks>
public static class FraudDetectionCalculator
{
    /// <summary>
    /// <c>|quoted - market| / market</c> — the deviation magnitude compared
    /// against <c>price_deviation_threshold</c> (02 §14.4). Returns
    /// <c>null</c> when no comparable market price exists; the caller treats
    /// that as "no signal" (rule disabled).
    /// </summary>
    public static decimal? CalculatePriceDeviation(decimal quotedPrice, decimal? marketPrice)
    {
        if (marketPrice is null or <= 0) return null;
        return Math.Abs(quotedPrice - marketPrice.Value) / marketPrice.Value;
    }

    /// <summary>
    /// True when either threshold is exceeded by the seller's recent activity
    /// (02 §14.4 "kısa sürede yüksek hacim"). A <c>null</c> threshold disables
    /// that branch; both null means the rule is effectively off and the
    /// helper returns <c>false</c>.
    /// </summary>
    /// <param name="recentTransactionCount">Count of seller transactions inside the window.</param>
    /// <param name="recentTotalAmount">Sum of <c>TotalAmount</c> inside the window.</param>
    /// <param name="countThreshold"><c>high_volume_count_threshold</c>; <c>null</c> when unconfigured.</param>
    /// <param name="amountThreshold"><c>high_volume_amount_threshold</c>; <c>null</c> when unconfigured.</param>
    public static bool IsHighVolume(
        int recentTransactionCount,
        decimal recentTotalAmount,
        int? countThreshold,
        decimal? amountThreshold)
    {
        if (countThreshold.HasValue && countThreshold.Value > 0
            && recentTransactionCount > countThreshold.Value)
            return true;

        if (amountThreshold.HasValue && amountThreshold.Value > 0
            && recentTotalAmount > amountThreshold.Value)
            return true;

        return false;
    }

    /// <summary>
    /// True when a long-standing, never-traded account suddenly attempts a
    /// high-value transaction (02 §14.3 "hiç işlem yapmayan hesabın aniden
    /// yüksek hacimli işlem yapması"). All three conditions must hold:
    /// <list type="bullet">
    ///   <item><c>completedTransactionCount == 0</c> (truly never traded);</item>
    ///   <item><c>accountAgeDays >= dormantMinAgeDays</c> (excludes brand new
    ///         accounts already covered by T39 new-account limits);</item>
    ///   <item><c>attemptedAmount > valueThreshold</c> (only "high value"
    ///         attempts trip — small reactivation transactions stay clean).</item>
    /// </list>
    /// A non-positive <paramref name="valueThreshold"/> disables the rule.
    /// </summary>
    public static bool IsDormantAnomaly(
        int completedTransactionCount,
        int accountAgeDays,
        decimal attemptedAmount,
        int dormantMinAgeDays,
        decimal valueThreshold)
    {
        if (valueThreshold <= 0) return false;
        if (completedTransactionCount > 0) return false;
        if (accountAgeDays < dormantMinAgeDays) return false;
        return attemptedAmount > valueThreshold;
    }
}
