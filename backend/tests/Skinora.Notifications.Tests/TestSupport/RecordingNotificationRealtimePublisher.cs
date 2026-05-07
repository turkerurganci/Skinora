using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;

namespace Skinora.Notifications.Tests.TestSupport;

/// <summary>
/// Test double recording every <see cref="INotificationRealtimePublisher"/>
/// call so dispatcher / inbox tests can assert payload content without
/// spinning up SignalR. Captures (method, userId, payload) tuples.
/// </summary>
public sealed class RecordingNotificationRealtimePublisher : INotificationRealtimePublisher
{
    public List<(string Method, Guid? UserId, object Payload)> Calls { get; } = [];

    public Task PublishNewNotificationAsync(
        Guid userId,
        NotificationRealtimePayloads.NewNotification payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("NewNotification", userId, payload));
        return Task.CompletedTask;
    }

    public Task PublishUnreadCountChangedAsync(
        Guid userId,
        NotificationRealtimePayloads.UnreadCountChanged payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("UnreadCountChanged", userId, payload));
        return Task.CompletedTask;
    }

    public Task PublishTelegramConnectedAsync(
        Guid userId,
        NotificationRealtimePayloads.TelegramConnected payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("TelegramConnected", userId, payload));
        return Task.CompletedTask;
    }

    public Task PublishDiscordConnectedAsync(
        Guid userId,
        NotificationRealtimePayloads.DiscordConnected payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("DiscordConnected", userId, payload));
        return Task.CompletedTask;
    }

    public Task PublishMaintenanceStatusChangedAsync(
        NotificationRealtimePayloads.MaintenanceStatusChanged payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("MaintenanceStatusChanged", null, payload));
        return Task.CompletedTask;
    }
}
