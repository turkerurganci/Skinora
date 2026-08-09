using Skinora.Shared.Enums;

namespace Skinora.Notifications.Application.Inbox;

/// <summary>
/// Derives the <c>targetType</c> + <c>targetId</c> pair returned by 07 §8.1.
/// The spec table maps each <see cref="NotificationType"/> to either
/// <c>"transaction"</c>, <c>"flag"</c> or <c>null</c>; user-facing types in
/// the MVP all carry a Transaction reference, so the storage column
/// <see cref="Domain.Entities.Notification.TransactionId"/> covers them.
/// Admin-only types (<c>ADMIN_FLAG_ALERT</c> vb.)
/// only show up on admin inboxes — <c>ADMIN_FLAG_ALERT</c> resolves to its
/// dedicated <see cref="Domain.Entities.Notification.FlagId"/> column (WP8).
/// </summary>
public static class NotificationTargetMapper
{
    public static (string? TargetType, Guid? TargetId) Resolve(
        NotificationType type, Guid? transactionId, Guid? flagId = null) => type switch
        {
            // Admin-only — platform health outage is a platform-wide alert (WP16).
            NotificationType.ADMIN_PLATFORM_OUTAGE => (null, null),

            // Admin-only — flag queue link, keyed by the dedicated FlagId
            // column (WP8 replaced the earlier TransactionId reinterpretation).
            NotificationType.ADMIN_FLAG_ALERT => flagId is null ? (null, null) : ("flag", flagId),

            // Every other type targets a Transaction when one is attached.
            _ => transactionId is null ? (null, null) : ("transaction", transactionId),
        };
}
