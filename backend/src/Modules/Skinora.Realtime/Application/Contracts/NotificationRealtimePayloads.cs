namespace Skinora.Realtime.Application.Contracts;

/// <summary>
/// Server→client payloads pushed on <c>/hubs/notifications</c> per 07 §11.2.
/// All payloads are camel-cased on the wire by the SignalR JSON protocol
/// (configured in <c>Program.cs</c> with <c>JsonStringEnumConverter</c>).
/// </summary>
public static class NotificationRealtimePayloads
{
    /// <summary>
    /// Pushed when a fresh <see cref="Skinora.Notifications.Domain.Entities.Notification"/>
    /// row lands in the user's inbox. Field set matches the
    /// <see cref="Skinora.Notifications.Application.Inbox.NotificationListItemDto"/>
    /// minus the <c>isRead</c> flag (a brand-new row is always unread).
    /// </summary>
    public sealed record NewNotification(
        Guid Id,
        string Type,
        string Message,
        string? TargetType,
        Guid? TargetId,
        DateTime CreatedAt);

    /// <summary>
    /// Pushed every time the user's unread-notification count changes
    /// (new notification, mark-read, mark-all-read).
    /// </summary>
    public sealed record UnreadCountChanged(int UnreadCount);

    /// <summary>
    /// Pushed when the user finishes the Telegram bot link flow (T79
    /// forward-deferred — webhook fires this from the <c>/start</c> handler).
    /// </summary>
    public sealed record TelegramConnected(string Username);

    /// <summary>
    /// Pushed when the user finishes the Discord OAuth flow (T80
    /// forward-deferred — callback fires this after token exchange).
    /// </summary>
    public sealed record DiscordConnected(string Username);

    /// <summary>
    /// Pushed when the platform maintenance status changes. Frontend renders
    /// the C08 banner (04 §7.7) and freezes timeouts when <c>active</c>
    /// transitions to <c>true</c>. T-future maintenance toggle endpoint will
    /// fire this; T62 wires the publisher and payload only.
    /// </summary>
    public sealed record MaintenanceStatusChanged(
        bool Active,
        string? Type,
        string? Message,
        DateTime? PlannedEnd);
}
