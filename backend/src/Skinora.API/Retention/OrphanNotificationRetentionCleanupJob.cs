using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Domain.Entities;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;

namespace Skinora.API.Retention;

/// <summary>
/// Recurring hard-delete sweep for orphan notifications — task T63b acceptance
/// criterion "Bağımsız bildirimler (Notification, TransactionId = NULL) + ilgili
/// NotificationDelivery kayıtları retention süresi sonrası toplu purge"
/// (06 §1, §6.1).
/// </summary>
/// <remarks>
/// <para>
/// Only notifications with <see cref="Notification.TransactionId"/> NULL are
/// candidates. Transaction-bound notifications follow the transaction's own
/// archive lifecycle (06 §1) and are out of scope for this sweep.
/// </para>
/// <para>
/// Delete order is fixed: NotificationDelivery → Notification. The delivery
/// table has an FK to Notification, so a notification cannot be removed while
/// its delivery rows still reference it. The sweep selects a batch of
/// candidate notification IDs, deletes all their delivery rows in a single
/// query, then deletes the notifications.
/// </para>
/// <para>
/// Retention is age-based (CreatedAt &lt; threshold) and ignores the
/// <see cref="Microsoft.EntityFrameworkCore.RelationalQueryableExtensions"/>
/// soft-delete filter — once a notification crosses the retention window it
/// is hard-purged regardless of <see cref="Notification.IsDeleted"/>.
/// </para>
/// </remarks>
public sealed class OrphanNotificationRetentionCleanupJob
{
    public const string RecurringJobId = "orphan-notification-retention-cleanup";

    public const string RetentionDaysKey = "retention.orphan_notification_days";
    public const string BatchSizeKey = "retention.batch_size_notification";

    public const int DefaultRetentionDays = 365;
    public const int DefaultBatchSize = 500;

    private readonly AppDbContext _db;
    private readonly ILogger<OrphanNotificationRetentionCleanupJob> _logger;

    public OrphanNotificationRetentionCleanupJob(
        AppDbContext db,
        ILogger<OrphanNotificationRetentionCleanupJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public void Execute() => ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();

    public async Task<OrphanNotificationSweepResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var retentionDays = await ReadSettingAsync(
            RetentionDaysKey, DefaultRetentionDays, cancellationToken);
        var batchSize = await ReadSettingAsync(
            BatchSizeKey, DefaultBatchSize, cancellationToken);
        var threshold = DateTime.UtcNow - TimeSpan.FromDays(retentionDays);

        var totalNotifications = 0;
        var totalDeliveries = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var notificationIds = await _db.Set<Notification>()
                .IgnoreQueryFilters()
                .Where(n => n.TransactionId == null && n.CreatedAt < threshold)
                .OrderBy(n => n.CreatedAt)
                .Take(batchSize)
                .Select(n => n.Id)
                .ToListAsync(cancellationToken);

            if (notificationIds.Count == 0) break;

            var deliveryDeleted = await _db.Set<NotificationDelivery>()
                .Where(d => notificationIds.Contains(d.NotificationId))
                .ExecuteDeleteAsync(cancellationToken);

            var notificationDeleted = await _db.Set<Notification>()
                .IgnoreQueryFilters()
                .Where(n => notificationIds.Contains(n.Id))
                .ExecuteDeleteAsync(cancellationToken);

            totalDeliveries += deliveryDeleted;
            totalNotifications += notificationDeleted;

            if (notificationDeleted < batchSize) break;
        }

        var result = new OrphanNotificationSweepResult(totalNotifications, totalDeliveries);

        if (totalNotifications > 0 || totalDeliveries > 0)
        {
            _logger.LogInformation(
                "OrphanNotificationRetentionCleanupJob purged {Notifications} orphan notification(s) and {Deliveries} delivery row(s) past the {RetentionDays}d window.",
                totalNotifications, totalDeliveries, retentionDays);
        }
        else
        {
            _logger.LogDebug(
                "OrphanNotificationRetentionCleanupJob found no eligible rows (retention window {RetentionDays}d).",
                retentionDays);
        }

        return result;
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

/// <summary>Counts emitted by a single orphan-notification sweep.</summary>
public sealed record OrphanNotificationSweepResult(
    int NotificationsDeleted,
    int DeliveriesDeleted);
