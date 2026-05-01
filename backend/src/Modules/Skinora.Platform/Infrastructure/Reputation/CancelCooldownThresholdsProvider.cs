using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;
using Skinora.Users.Application.Reputation;

namespace Skinora.Platform.Infrastructure.Reputation;

/// <summary>
/// SystemSetting-backed implementation of
/// <see cref="ICancelCooldownThresholdsProvider"/>. Returns a sentinel zero
/// when a row is unconfigured — callers treat zero as "rule disabled" so a
/// half-bootstrapped environment does not trip every user into cooldown.
/// </summary>
public sealed class CancelCooldownThresholdsProvider : ICancelCooldownThresholdsProvider
{
    public const string LimitCountKey = "cancel_limit_count";
    public const string WindowHoursKey = "cancel_limit_period_hours";
    public const string CooldownHoursKey = "cancel_cooldown_hours";

    private readonly AppDbContext _db;

    public CancelCooldownThresholdsProvider(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CancelCooldownThresholds> GetAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => (s.Key == LimitCountKey || s.Key == WindowHoursKey || s.Key == CooldownHoursKey)
                        && s.IsConfigured)
            .Select(s => new { s.Key, s.Value })
            .ToDictionaryAsync(r => r.Key, r => r.Value, cancellationToken);

        return new CancelCooldownThresholds(
            LimitCount: ReadPositiveInt(rows, LimitCountKey),
            WindowHours: ReadPositiveInt(rows, WindowHoursKey),
            CooldownHours: ReadPositiveInt(rows, CooldownHoursKey));
    }

    private static int ReadPositiveInt(IReadOnlyDictionary<string, string?> rows, string key)
    {
        if (!rows.TryGetValue(key, out var raw)) return 0;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : 0;
    }
}
