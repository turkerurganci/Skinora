using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;
using Skinora.Users.Application.Reputation;

namespace Skinora.Platform.Infrastructure.Reputation;

/// <summary>
/// SystemSetting-backed implementation. Mirrors the
/// <c>SettingsBasedAgeGateCheck</c> read pattern (T30) — direct AsNoTracking
/// query, no caching layer (admin updates take effect immediately).
/// </summary>
public sealed class ReputationThresholdsProvider : IReputationThresholdsProvider
{
    public const string MinAccountAgeDaysKey = "reputation.min_account_age_days";
    public const string MinCompletedTransactionsKey = "reputation.min_completed_transactions";

    // Documented defaults (02 §13, 06 §3.1). Used as safety net when a row is
    // missing or NULL — should never happen in a properly bootstrapped
    // environment but keeps the read path resilient against partial seed.
    public const int DefaultMinAccountAgeDays = 30;
    public const int DefaultMinCompletedTransactions = 3;

    private readonly AppDbContext _db;

    public ReputationThresholdsProvider(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ReputationThresholds> GetAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => (s.Key == MinAccountAgeDaysKey || s.Key == MinCompletedTransactionsKey)
                        && s.IsConfigured)
            .Select(s => new { s.Key, s.Value })
            .ToDictionaryAsync(r => r.Key, r => r.Value, cancellationToken);

        return new ReputationThresholds(
            MinAccountAgeDays: ReadPositiveInt(rows, MinAccountAgeDaysKey, DefaultMinAccountAgeDays),
            MinCompletedTransactions: ReadPositiveInt(rows, MinCompletedTransactionsKey, DefaultMinCompletedTransactions));
    }

    private static int ReadPositiveInt(IReadOnlyDictionary<string, string?> rows, string key, int fallback)
    {
        if (!rows.TryGetValue(key, out var raw)) return fallback;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            return parsed;
        return fallback;
    }
}
