using Skinora.Shared.Enums;

namespace Skinora.Notifications.Application.Inbox;

/// <summary>
/// Derives the <c>targetType</c> + <c>targetId</c> pair returned by 07 §8.1.
/// The spec table maps each <see cref="NotificationType"/> to either
/// <c>"transaction"</c>, <c>"flag"</c> or <c>null</c>; user-facing types in
/// the MVP all carry a Transaction reference, so the storage column
/// <see cref="Domain.Entities.Notification.TransactionId"/> is the only
/// payload we need today. Admin-only types (<c>ADMIN_FLAG_ALERT</c>,
/// <c>ADMIN_STEAM_BOT_ISSUE</c>) only show up on admin inboxes — the
/// dedicated mapping is documented here so the same helper covers them
/// when admin notification listing lands (T39+).
/// </summary>
public static class NotificationTargetMapper
{
    public static (string? TargetType, Guid? TargetId) Resolve(
        NotificationType type, Guid? transactionId) => type switch
        {
            // Admin-only — Steam bot incident is a platform-wide alert.
            NotificationType.ADMIN_STEAM_BOT_ISSUE => (null, null),

            // Admin-only — flag queue link. TransactionId column doubles as
            // FlagId today; admin endpoints (T39+) will swap in a dedicated
            // column if/when required.
            NotificationType.ADMIN_FLAG_ALERT => ("flag", transactionId),

            // Every other type targets a Transaction when one is attached.
            _ => transactionId is null ? (null, null) : ("transaction", transactionId),
        };
}
