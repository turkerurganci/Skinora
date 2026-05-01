using Skinora.Shared.Models;

namespace Skinora.Notifications.Application.Inbox;

/// <summary>
/// Read + mark-as-read operations backing the platform-in-app notification
/// channel (07 §8.1–§8.4 / 05 §7.2). All operations are scoped to the
/// authenticated user — global query filter on
/// <see cref="Skinora.Notifications.Domain.Entities.Notification"/> already
/// hides soft-deleted rows.
/// </summary>
public interface INotificationInboxService
{
    /// <summary>N1 — paginated bildirim list (newest first).</summary>
    Task<PagedResult<NotificationListItemDto>> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>N2 — unread count for the S05 header badge.</summary>
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>N3 — flip every unread notification to <c>IsRead=true</c>.</summary>
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>N4 — flip a single notification owned by the caller.</summary>
    Task<MarkReadOutcome> MarkReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken);
}
