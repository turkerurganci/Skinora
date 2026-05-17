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

    /// <summary>
    /// Broadcast variant: bot status changes are visible to every admin client
    /// (T69 — 02 §15). The frontend filters on the admin route guard; we do
    /// not yet have a per-role group abstraction, so the fanout is platform-
    /// wide and admin UI consumes the event while other roles ignore it.
    /// Best-effort like all realtime pushes — audit log row written next to
    /// the publish is the durable record.
    /// </summary>
    Task PublishAdminBotStatusChangedAsync(
        NotificationRealtimePayloads.AdminBotStatusChanged payload,
        CancellationToken cancellationToken);

    /// <summary>
    /// Broadcast variant: reconciliation mismatches are visible to every admin
    /// client (T76 — 05 §3.3). Fired once per (scope, token) finding by
    /// <see cref="Skinora.Transactions.Application.Reconciliation.IReconciliationService"/>.
    /// Best-effort like all realtime pushes — the
    /// <c>RECONCILIATION_MISMATCH</c> AuditLog row written next to the
    /// publish is the durable record.
    /// </summary>
    Task PublishAdminReconciliationMismatchAsync(
        NotificationRealtimePayloads.AdminReconciliationMismatch payload,
        CancellationToken cancellationToken);

    /// <summary>
    /// Broadcast variant: hot wallet threshold breaches surface on every
    /// admin client (T77 — 05 §3.3). Fired once per (token, direction)
    /// finding by the
    /// <see cref="Skinora.Transactions.Application.Wallets.IHotWalletMonitorService"/>
    /// run. Best-effort; the <c>HOT_WALLET_THRESHOLD_BREACHED</c> AuditLog
    /// row written next to the publish is the durable record.
    /// </summary>
    Task PublishAdminHotWalletThresholdBreachedAsync(
        NotificationRealtimePayloads.AdminHotWalletThresholdBreached payload,
        CancellationToken cancellationToken);
}
