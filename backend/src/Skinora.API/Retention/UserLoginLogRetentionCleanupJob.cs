using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Retention;

/// <summary>
/// Recurring hard-delete sweep for <see cref="UserLoginLog"/> rows past their
/// retention window — task T63b acceptance criterion "Soft-deleted entity'ler
/// için retention-based hard purge (06 §8.2 lifecycle)". UserLoginLog is the
/// only soft-deletable user-scoped log entity with an age-based hard purge
/// (06 §1, §6.1 — 1 yıl default).
/// </summary>
/// <remarks>
/// <para>
/// Retention is age-based on <see cref="UserLoginLog.CreatedAt"/> and ignores
/// the global soft-delete filter — once a log row crosses the retention
/// window it is purged regardless of <see cref="Skinora.Shared.Domain.ISoftDeletable.IsDeleted"/>.
/// </para>
/// <para>
/// UserLoginLog has no dependent rows, so the sweep is a single SELECT+DELETE
/// per batch.
/// </para>
/// </remarks>
public sealed class UserLoginLogRetentionCleanupJob
{
    public const string RecurringJobId = "user-login-log-retention-cleanup";

    public const string RetentionDaysKey = "retention.user_login_log_days";
    public const string BatchSizeKey = "retention.batch_size_user_login_log";

    public const int DefaultRetentionDays = 365;
    public const int DefaultBatchSize = 1000;

    private readonly AppDbContext _db;
    private readonly ILogger<UserLoginLogRetentionCleanupJob> _logger;

    public UserLoginLogRetentionCleanupJob(
        AppDbContext db,
        ILogger<UserLoginLogRetentionCleanupJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public void Execute() => ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var retentionDays = await ReadSettingAsync(
            RetentionDaysKey, DefaultRetentionDays, cancellationToken);
        var batchSize = await ReadSettingAsync(
            BatchSizeKey, DefaultBatchSize, cancellationToken);
        var threshold = DateTime.UtcNow - TimeSpan.FromDays(retentionDays);

        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ids = await _db.Set<UserLoginLog>()
                .IgnoreQueryFilters()
                .Where(l => l.CreatedAt < threshold)
                .OrderBy(l => l.CreatedAt)
                .Take(batchSize)
                .Select(l => l.Id)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0) break;

            var deleted = await _db.Set<UserLoginLog>()
                .IgnoreQueryFilters()
                .Where(l => ids.Contains(l.Id))
                .ExecuteDeleteAsync(cancellationToken);

            total += deleted;
            if (deleted < batchSize) break;
        }

        if (total > 0)
        {
            _logger.LogInformation(
                "UserLoginLogRetentionCleanupJob purged {Count} login log row(s) past the {RetentionDays}d window.",
                total, retentionDays);
        }
        else
        {
            _logger.LogDebug(
                "UserLoginLogRetentionCleanupJob found no eligible rows (retention window {RetentionDays}d).",
                retentionDays);
        }

        return total;
    }

    private async Task<int> ReadSettingAsync(
        string key, int defaultValue, CancellationToken cancellationToken)
    {
        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == key && s.IsConfigured)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
               && parsed > 0
            ? parsed
            : defaultValue;
    }
}
