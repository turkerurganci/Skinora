using Skinora.Realtime.Application.Contracts;

namespace Skinora.Realtime.Application;

/// <summary>
/// Publishes server→client events on the <c>/hubs/notifications</c> channel
/// (T62 — 07 §11.2 RT2). Implementations target the per-user group
/// <c>user:{userId:N}</c>; every connection a user holds (multiple tabs,
/// devices) receives the push.
/// </summary>
/// <remarks>
/// All methods are best-effort fire-and-forget at the application boundary:
/// failures (no subscribers, transport errors) must not propagate as
/// exceptions to the calling consumer / dispatcher because the outbox would
/// interpret an exception as a redelivery signal and the inbox service would
/// roll back a successful read-state mutation. Concrete adapters log and
/// swallow.
/// </remarks>
public interface INotificationRealtimePublisher
{
    Task PublishNewNotificationAsync(
        Guid userId,
        NotificationRealtimePayloads.NewNotification payload,
        CancellationToken cancellationToken);

    Task PublishUnreadCountChangedAsync(
        Guid userId,
        NotificationRealtimePayloads.UnreadCountChanged payload,
        CancellationToken cancellationToken);

    Task PublishTelegramConnectedAsync(
        Guid userId,
        NotificationRealtimePayloads.TelegramConnected payload,
        CancellationToken cancellationToken);

    Task PublishDiscordConnectedAsync(
        Guid userId,
        NotificationRealtimePayloads.DiscordConnected payload,
        CancellationToken cancellationToken);

    /// <summary>
    /// Broadcast variant: maintenance status is platform-wide. Implementations
    /// target every connected client regardless of user.
    /// </summary>
    Task PublishMaintenanceStatusChangedAsync(
        NotificationRealtimePayloads.MaintenanceStatusChanged payload,
        CancellationToken cancellationToken);
}
