using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;
using Skinora.Users.Application.Reputation;

namespace Skinora.Platform.Infrastructure.Reputation;

/// <summary>
/// SystemSetting-backed implementation of
/// <see cref="INonDeliveryAbuseThresholdsProvider"/>. Same sentinel as
/// <see cref="CancelCooldownThresholdsProvider"/>: an unconfigured or
/// unparseable row reads as zero, and zero disables the rule
/// (<see cref="NonDeliveryAbuseThresholds.IsEnabled"/>).
/// </summary>
public sealed class NonDeliveryAbuseThresholdsProvider : INonDeliveryAbuseThresholdsProvider
{
    public const string WindowDaysKey = "non_delivery_window_days";
    public const string FlagCountKey = "non_delivery_flag_count";
    public const string SuspendCountKey = "non_delivery_suspend_count";

    private readonly AppDbContext _db;

    public NonDeliveryAbuseThresholdsProvider(AppDbContext db)
    {
        _db = db;
    }

    public async Task<NonDeliveryAbuseThresholds> GetAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => (s.Key == WindowDaysKey || s.Key == FlagCountKey || s.Key == SuspendCountKey)
                        && s.IsConfigured)
            .Select(s => new { s.Key, s.Value })
            .ToDictionaryAsync(r => r.Key, r => r.Value, cancellationToken);

        return new NonDeliveryAbuseThresholds(
            WindowDays: ReadPositiveInt(rows, WindowDaysKey),
            FlagCount: ReadPositiveInt(rows, FlagCountKey),
            SuspendCount: ReadPositiveInt(rows, SuspendCountKey));
    }

    private static int ReadPositiveInt(IReadOnlyDictionary<string, string?> rows, string key)
    {
        if (!rows.TryGetValue(key, out var raw)) return 0;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : 0;
    }
}
