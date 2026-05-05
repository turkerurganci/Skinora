using Skinora.Users.Application.MultiAccount;

namespace Skinora.Fraud.Application.MultiAccount;

/// <summary>
/// Pure helpers for the multi-account detection rules (T56 — 02 §14.3,
/// 03 §7.4). Lifted out of <see cref="MultiAccountDetector"/> so the matching
/// logic can be exercised by fast unit tests without spinning up a database.
/// </summary>
public static class MultiAccountSignalEvaluator
{
    /// <summary>
    /// Parse the admin-curated exchange/custodial allowlist CSV stored in
    /// <see cref="MultiAccountDetector.ExchangeAddressesSettingKey"/>.
    /// Handles the <see cref="MultiAccountDetector.ExchangeAddressesNoneMarker"/>
    /// sentinel ("NONE" = empty list) and trims surrounding whitespace per
    /// token. Comparison is exact (case-sensitive); TRC-20 addresses are
    /// case-mixed but always normalised by the wallet validator before
    /// reaching the SystemSetting row.
    /// </summary>
    /// <param name="raw">The CSV stored in the SystemSetting <c>Value</c> column. <c>null</c> / blank / "NONE" → empty set.</param>
    public static HashSet<string> ParseExchangeAddresses(string? raw)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw)
            || string.Equals(raw.Trim(), MultiAccountDetector.ExchangeAddressesNoneMarker, StringComparison.Ordinal))
        {
            return set;
        }
        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(token);
        }
        return set;
    }

    /// <summary>
    /// Decide which strong-signal match wins when both wallet roles match.
    /// Payout (where seller funds settle) takes priority over refund — both
    /// are equally strong per 02 §14.3, but the payout match is the more
    /// actionable evidence for the admin reviewer.
    /// </summary>
    /// <param name="hasPayoutMatch">True when at least one other account shares the payout address.</param>
    /// <param name="hasRefundMatch">True when at least one other account shares the refund address.</param>
    /// <returns>The winning match type, or <c>null</c> if neither role matched.</returns>
    public static MultiAccountMatchType? PickStrongMatchType(
        bool hasPayoutMatch, bool hasRefundMatch)
    {
        if (hasPayoutMatch) return MultiAccountMatchType.WALLET_PAYOUT;
        if (hasRefundMatch) return MultiAccountMatchType.WALLET_REFUND;
        return null;
    }
}
