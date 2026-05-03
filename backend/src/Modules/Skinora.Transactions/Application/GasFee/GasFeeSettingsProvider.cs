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

    private static readonly string[] _allKeys =
    [
        ProtectionRatioKey,
        MinRefundThresholdRatioKey,
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
            MinRefundThresholdRatio: ReadMinRefundThresholdRatio(rows));
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
}
