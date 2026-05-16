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
/// <see cref="NotificationsHub.GroupName(Guid)"/>. The <c>Maintenance</c>
/// variant fans out to <see cref="IHubClients.All"/> because the C08 banner
/// is platform-wide.
/// </summary>
public sealed class SignalRNotificationRealtimePublisher : INotificationRealtimePublisher
{
    private const string NewNotificationEvent = "NewNotification";
    private const string UnreadCountChangedEvent = "UnreadCountChanged";
    private const string TelegramConnectedEvent = "TelegramConnected";
    private const string DiscordConnectedEvent = "DiscordConnected";
    private const string MaintenanceStatusChangedEvent = "MaintenanceStatusChanged";
    private const string AdminBotStatusChangedEvent = "AdminBotStatusChanged";

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

    public async Task PublishMaintenanceStatusChangedAsync(
        NotificationRealtimePayloads.MaintenanceStatusChanged payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await _hub.Clients
                .All
                .SendAsync(MaintenanceStatusChangedEvent, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SignalR maintenance push failed.");
        }
    }

    public async Task PublishAdminBotStatusChangedAsync(
        NotificationRealtimePayloads.AdminBotStatusChanged payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await _hub.Clients
                .All
                .SendAsync(AdminBotStatusChangedEvent, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            // Persistence (PlatformSteamBot.Status update + AuditLog row) is
            // the source of truth; a missed push is recovered on admin
            // dashboard refresh.
            _logger.LogWarning(
                ex,
                "SignalR admin bot status push failed (botId={BotId}, status={Status}).",
                payload.BotId, payload.NewStatus);
        }
    }

    private async Task SendToUserAsync(
        Guid userId,
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await _hub.Clients
                .Group(NotificationsHub.GroupName(userId))
                .SendAsync(method, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            // Realtime delivery is best-effort — frontend re-fetches the inbox
            // on reconnect (T96), so the caller (dispatcher / inbox service)
            // must not surface this as a failure that aborts the surrounding
            // unit of work.
            _logger.LogWarning(
                ex,
                "SignalR push failed for user {UserId} method {Method}.",
                userId, method);
        }
    }
}
