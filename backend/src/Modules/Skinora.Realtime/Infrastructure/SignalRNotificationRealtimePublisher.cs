using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;
using Skinora.Realtime.Hubs;

namespace Skinora.Realtime.Infrastructure;

/// <summary>
/// SignalR-backed implementation of <see cref="INotificationRealtimePublisher"/>.
/// Resolves <see cref="IHubContext{T}"/> for <see cref="NotificationsHub"/> and
/// pushes payloads to the per-user group named by
/// <see cref="NotificationsHub.GroupName(Guid)"/>. The three admin-scoped events
/// target <see cref="NotificationsHub.AdminGroup"/> (T69 K4 — only admins join
/// it). The <c>Maintenance</c> variant fans out to <see cref="IHubClients.All"/>
/// because the C08 banner is platform-wide. Every send is best-effort: failures
/// are logged uniformly (WP9 group-failure observability) and swallowed so a
/// dropped push never propagates to the outbox dispatcher as a redelivery signal.
/// </summary>
public sealed class SignalRNotificationRealtimePublisher : INotificationRealtimePublisher
{
    private const string NewNotificationEvent = "NewNotification";
    private const string UnreadCountChangedEvent = "UnreadCountChanged";
    private const string TelegramConnectedEvent = "TelegramConnected";
    private const string DiscordConnectedEvent = "DiscordConnected";
    private const string MaintenanceStatusChangedEvent = "MaintenanceStatusChanged";
    private const string AdminBotStatusChangedEvent = "AdminBotStatusChanged";
    private const string AdminReconciliationMismatchEvent = "AdminReconciliationMismatch";
    private const string AdminHotWalletThresholdBreachedEvent = "AdminHotWalletThresholdBreached";

    private readonly IHubContext<NotificationsHub> _hub;
    private readonly ILogger<SignalRNotificationRealtimePublisher> _logger;

    public SignalRNotificationRealtimePublisher(
        IHubContext<NotificationsHub> hub,
        ILogger<SignalRNotificationRealtimePublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task PublishNewNotificationAsync(
        Guid userId,
        NotificationRealtimePayloads.NewNotification payload,
        CancellationToken cancellationToken) =>
        SendToUserAsync(userId, NewNotificationEvent, payload, cancellationToken);

    public Task PublishUnreadCountChangedAsync(
        Guid userId,
        NotificationRealtimePayloads.UnreadCountChanged payload,
        CancellationToken cancellationToken) =>
        SendToUserAsync(userId, UnreadCountChangedEvent, payload, cancellationToken);

    public Task PublishTelegramConnectedAsync(
        Guid userId,
        NotificationRealtimePayloads.TelegramConnected payload,
        CancellationToken cancellationToken) =>
        SendToUserAsync(userId, TelegramConnectedEvent, payload, cancellationToken);

    public Task PublishDiscordConnectedAsync(
        Guid userId,
        NotificationRealtimePayloads.DiscordConnected payload,
        CancellationToken cancellationToken) =>
        SendToUserAsync(userId, DiscordConnectedEvent, payload, cancellationToken);

    public Task PublishMaintenanceStatusChangedAsync(
        NotificationRealtimePayloads.MaintenanceStatusChanged payload,
        CancellationToken cancellationToken) =>
        // Platform-wide banner — genuinely everyone (07 §11.2 C08).
        SendToAllAsync(MaintenanceStatusChangedEvent, payload, cancellationToken);

    public Task PublishAdminBotStatusChangedAsync(
        NotificationRealtimePayloads.AdminBotStatusChanged payload,
        CancellationToken cancellationToken) =>
        SendToGroupAsync(NotificationsHub.AdminGroup, AdminBotStatusChangedEvent, payload, cancellationToken);

    public Task PublishAdminReconciliationMismatchAsync(
        NotificationRealtimePayloads.AdminReconciliationMismatch payload,
        CancellationToken cancellationToken) =>
        SendToGroupAsync(NotificationsHub.AdminGroup, AdminReconciliationMismatchEvent, payload, cancellationToken);

    public Task PublishAdminHotWalletThresholdBreachedAsync(
        NotificationRealtimePayloads.AdminHotWalletThresholdBreached payload,
        CancellationToken cancellationToken) =>
        SendToGroupAsync(NotificationsHub.AdminGroup, AdminHotWalletThresholdBreachedEvent, payload, cancellationToken);

    private Task SendToUserAsync(
        Guid userId,
        string method,
        object payload,
        CancellationToken cancellationToken) =>
        SendToGroupAsync(NotificationsHub.GroupName(userId), method, payload, cancellationToken);

    private async Task SendToGroupAsync(
        string group,
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await _hub.Clients
                .Group(group)
                .SendAsync(method, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort: the durable record (Notification row / AuditLog row)
            // is the source of truth; a dropped push is recovered on the next
            // page refresh / reconnect (T96). Uniform structured logging (WP9
            // group-failure observability) keeps every miss queryable in Loki.
            _logger.LogWarning(
                ex,
                "SignalR group push failed for group {Group} method {Method}.",
                group, method);
        }
    }

    private async Task SendToAllAsync(
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await _hub.Clients
                .All
                .SendAsync(method, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SignalR broadcast push failed for method {Method}.",
                method);
        }
    }
}
