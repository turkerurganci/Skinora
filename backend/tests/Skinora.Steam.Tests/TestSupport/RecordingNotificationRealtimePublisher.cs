using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;

namespace Skinora.Steam.Tests.TestSupport;

/// <summary>
/// Local recording double used by T69 webhook tests so we can assert that the
/// admin bot-status SignalR broadcast actually fired without spinning up the
/// SignalR runtime. Mirrors the Notifications.Tests recorder but lives here
/// to keep the test-project dependency graph minimal.
/// </summary>
public sealed class RecordingNotificationRealtimePublisher : INotificationRealtimePublisher
{
    public List<(string Method, object Payload)> Calls { get; } = [];

    public Task PublishNewNotificationAsync(
        Guid userId,
        NotificationRealtimePayloads.NewNotification payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("NewNotification", payload));
        return Task.CompletedTask;
    }

    public Task PublishUnreadCountChangedAsync(
        Guid userId,
        NotificationRealtimePayloads.UnreadCountChanged payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("UnreadCountChanged", payload));
        return Task.CompletedTask;
    }

    public Task PublishTelegramConnectedAsync(
        Guid userId,
        NotificationRealtimePayloads.TelegramConnected payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("TelegramConnected", payload));
        return Task.CompletedTask;
    }

    public Task PublishDiscordConnectedAsync(
        Guid userId,
        NotificationRealtimePayloads.DiscordConnected payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("DiscordConnected", payload));
        return Task.CompletedTask;
    }

    public Task PublishMaintenanceStatusChangedAsync(
        NotificationRealtimePayloads.MaintenanceStatusChanged payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("MaintenanceStatusChanged", payload));
        return Task.CompletedTask;
    }

    public Task PublishAdminBotStatusChangedAsync(
        NotificationRealtimePayloads.AdminBotStatusChanged payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("AdminBotStatusChanged", payload));
        return Task.CompletedTask;
    }

    public Task PublishAdminReconciliationMismatchAsync(
        NotificationRealtimePayloads.AdminReconciliationMismatch payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("AdminReconciliationMismatch", payload));
        return Task.CompletedTask;
    }
}
