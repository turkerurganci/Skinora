using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// SystemSetting-backed retry policy reader. Parses the CSV stored in
/// <c>blockchain.transfer_retry_intervals_minutes</c> (T73) on each call;
/// no caching so an admin tweak via <c>PATCH /admin/settings</c> is visible
/// to the dispatcher on the very next tick.
///
/// <para>
/// Malformed / unconfigured rows fall back to the documented default
/// (<c>1, 5, 15</c> minutes) so a poisoned row cannot cause the dispatcher
/// to either give up too quickly or loop forever.
/// </para>
/// </summary>
public sealed class SystemSettingsTransferRetryPolicy : ITransferRetryPolicy
{
    public const string IntervalsKey = "blockchain.transfer_retry_intervals_minutes";

    /// <summary>Documented MVP default — 1dk, 5dk, 15dk (08 §3.3, 05 §3.3).</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultIntervals =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];

    private readonly AppDbContext _db;

    public SystemSettingsTransferRetryPolicy(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetMaxAttemptsAsync(CancellationToken cancellationToken)
    {
        var intervals = await ReadIntervalsAsync(cancellationToken);
        // First attempt + one attempt per configured interval.
        return intervals.Count + 1;
    }

    public async Task<TimeSpan?> GetRetryDelayAsync(int retryCount, CancellationToken cancellationToken)
    {
        if (retryCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryCount), retryCount, "RetryCount must be non-negative.");
        }
        var intervals = await ReadIntervalsAsync(cancellationToken);
        if (retryCount >= intervals.Count) return null;
        return intervals[retryCount];
    }

    private async Task<IReadOnlyList<TimeSpan>> ReadIntervalsAsync(CancellationToken cancellationToken)
    {
        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == IntervalsKey && s.IsConfigured)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw)) return DefaultIntervals;

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return DefaultIntervals;

        var intervals = new List<TimeSpan>(parts.Length);
        foreach (var part in parts)
        {
            if (!decimal.TryParse(part, NumberStyles.Number, CultureInfo.InvariantCulture, out var minutes)
                || minutes <= 0m)
            {
                return DefaultIntervals;
            }
            intervals.Add(TimeSpan.FromMinutes((double)minutes));
        }
        return intervals;
    }
}
