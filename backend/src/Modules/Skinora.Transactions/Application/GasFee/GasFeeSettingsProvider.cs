using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Calculations;

namespace Skinora.Transactions.Application.GasFee;

/// <summary>
/// SystemSetting-backed live reader for gas-fee parameters. Mirrors
/// <c>TransactionLimitsProvider</c> (T45) and <c>ReputationThresholdsProvider</c>
/// (T43): direct <c>AsNoTracking</c> dictionary fetch, no caching, falls back
/// to the documented defaults when a row is missing or malformed so the
/// refund decision path stays resilient against partial seed.
/// </summary>
public sealed class GasFeeSettingsProvider : IGasFeeSettingsProvider
{
    public const string ProtectionRatioKey = "gas_fee_protection_ratio";
    public const string MinRefundThresholdRatioKey = "min_refund_threshold_ratio";
    public const string RefundGasFeeEstimateKey = "blockchain.refund_gas_fee_estimate_usdt";
    public const string PayoutGasFeeEstimateKey = "blockchain.payout_gas_fee_estimate_usdt";

    /// <summary>
    /// Code-side fallback for <see cref="RefundGasFeeEstimateKey"/> — mirrors
    /// the seeded default of <c>2.0</c> USDT (T72 MVP). Provider falls back
    /// to this when the row is missing, unconfigured, or malformed.
    /// </summary>
    public const decimal DefaultRefundGasFeeEstimateUsdt = 2.0m;

    /// <summary>
    /// Code-side fallback for <see cref="PayoutGasFeeEstimateKey"/> — mirrors
    /// the seeded default of <c>0.50</c> USDT (WP1 MVP, 04 §7.3 worked
    /// example). Provider falls back to this when the row is missing,
    /// unconfigured, or malformed.
    /// </summary>
    public const decimal DefaultPayoutGasFeeEstimateUsdt = 0.50m;

    public const string MaxChargedGasFeeKey = "blockchain.max_charged_gas_fee_usdt";

    /// <summary>
    /// Code-side fallback for <see cref="MaxChargedGasFeeKey"/>. Set well
    /// above any plausible real transfer cost (a mainnet TRC-20 send burns
    /// roughly 6.4 TRX, about 2 USDT) so it never fires on a healthy estimate
    /// — it exists to catch a broken one.
    /// </summary>
    public const decimal DefaultMaxChargedGasFeeUsdt = 10.0m;

    private static readonly string[] _allKeys =
    [
        ProtectionRatioKey,
        MinRefundThresholdRatioKey,
        RefundGasFeeEstimateKey,
        PayoutGasFeeEstimateKey,
        MaxChargedGasFeeKey,
    ];

    private readonly AppDbContext _db;

    public GasFeeSettingsProvider(AppDbContext db)
    {
        _db = db;
    }

    public async Task<GasFeeSettings> GetAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => _allKeys.Contains(s.Key) && s.IsConfigured)
            .Select(s => new { s.Key, s.Value })
            .ToDictionaryAsync(r => r.Key, r => r.Value, cancellationToken);

        return new GasFeeSettings(
            ProtectionRatio: ReadProtectionRatio(rows),
            MinRefundThresholdRatio: ReadMinRefundThresholdRatio(rows),
            RefundGasFeeEstimateUsdt: ReadRefundGasFeeEstimate(rows),
            PayoutGasFeeEstimateUsdt: ReadPayoutGasFeeEstimate(rows),
            MaxChargedGasFeeUsdt: ReadDecimal(rows, MaxChargedGasFeeKey, DefaultMaxChargedGasFeeUsdt));
    }

    private static decimal ReadRefundGasFeeEstimate(IReadOnlyDictionary<string, string?> rows)
    {
        // Validator stage 2 enforces > 0 (generic positive-number rule —
        // SystemSettingsValidator.cs default branch). Mirror that envelope
        // on the read side so a row poisoned out-of-band cannot drag the
        // refund threshold below the documented MVP floor.
        if (rows.TryGetValue(RefundGasFeeEstimateKey, out var raw)
            && raw is not null
            && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0m)
        {
            return parsed;
        }
        return DefaultRefundGasFeeEstimateUsdt;
    }

    private static decimal ReadPayoutGasFeeEstimate(IReadOnlyDictionary<string, string?> rows)
    {
        // Same read-side envelope as the refund estimate (generic positive-
        // number validator rule): a missing / unconfigured / poisoned row
        // falls back to the documented WP1 MVP default so the seller-payout
        // split can never run against a non-positive gas estimate.
        if (rows.TryGetValue(PayoutGasFeeEstimateKey, out var raw)
            && raw is not null
            && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0m)
        {
            return parsed;
        }
        return DefaultPayoutGasFeeEstimateUsdt;
    }

    private static decimal ReadProtectionRatio(IReadOnlyDictionary<string, string?> rows)
    {
        // Validator stage 2 enforces 0 < value < 1 — mirror that envelope on
        // the read side so a row poisoned out-of-band (manual SQL, restored
        // backup) cannot collapse the seller protection threshold.
        if (rows.TryGetValue(ProtectionRatioKey, out var raw)
            && raw is not null
            && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0m
            && parsed < 1m)
        {
            return parsed;
        }
        return FinancialCalculator.DefaultGasFeeProtectionRatio;
    }

    private static decimal ReadMinRefundThresholdRatio(IReadOnlyDictionary<string, string?> rows)
    {
        // Validator stage 2 enforces > 0 (multiplier legitimately exceeds 1
        // — default is 2.0, see SystemSettingsValidator.cs:165).
        if (rows.TryGetValue(MinRefundThresholdRatioKey, out var raw)
            && raw is not null
            && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0m)
        {
            return parsed;
        }
        return FinancialCalculator.DefaultMinimumRefundThresholdRatio;
    }

    /// <summary>
    /// Generic positive-decimal read with a documented default. Same read-side
    /// envelope as the estimates above: missing, unconfigured or poisoned rows
    /// fall back rather than propagate a nonsense value into a money path.
    /// </summary>
    private static decimal ReadDecimal(
        IReadOnlyDictionary<string, string?> rows, string key, decimal fallback)
    {
        if (rows.TryGetValue(key, out var raw)
            && raw is not null
            && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0m)
        {
            return parsed;
        }
        return fallback;
    }
}
