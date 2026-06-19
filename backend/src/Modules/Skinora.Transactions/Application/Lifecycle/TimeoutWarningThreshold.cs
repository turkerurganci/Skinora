using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// WP12 (T83a/T45) — shared reader for the timeout-warning threshold the
/// read-path timeout blocks surface (07 §7.1 list, 07 §7.5 detail). Resolves
/// the admin-tunable <c>timeout_warning_ratio</c> SystemSetting (02 §3.4,
/// 06 §3.17 — a decimal open-(0,1) ratio validated by
/// <c>SystemSettingsValidator</c>) to the integer percent the contract returns,
/// falling back to <see cref="DefaultPercent"/> when the row is unconfigured or
/// invalid. Centralised so <c>TransactionDetailService</c> and
/// <c>TransactionListService</c> share one source instead of each hard-coding
/// 75 (the prior T46/T83a duplicated const). The notification side of the same
/// ratio is consumed by <c>TimeoutSchedulingService</c>.
/// </summary>
internal static class TimeoutWarningThreshold
{
    /// <summary>06 §3.17 default (0.75 ratio → 75%) used when unconfigured.</summary>
    public const int DefaultPercent = 75;

    /// <summary>SystemSetting key — same canonical key the scheduler reads.</summary>
    public const string RatioKey = "timeout_warning_ratio";

    public static async Task<int> ReadPercentAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var raw = await db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == RatioKey && s.IsConfigured)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(raw)
            && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var ratio)
            && ratio > 0m && ratio < 1m)
        {
            return (int)Math.Round(ratio * 100m, MidpointRounding.AwayFromZero);
        }

        return DefaultPercent;
    }
}
