namespace Skinora.Notifications.Application.Inbox;

/// <summary>
/// Single bildirim row returned by 07 §8.1 (<c>GET /notifications</c>).
/// Field set is fixed by the spec — <c>message</c> maps to
/// <see cref="Domain.Entities.Notification.Title"/> (the short headline shown
/// in S11 04 §7.7), <c>type</c> is the UPPER_SNAKE_CASE enum name (07 §2.8)
/// and <c>targetType</c>/<c>targetId</c> are derived from the
/// <see cref="Skinora.Shared.Enums.NotificationType"/> table in 07 §8.1.
/// </summary>
public sealed record NotificationListItemDto(
    Guid Id,
    string Type,
    string Message,
    string? TargetType,
    Guid? TargetId,
    bool IsRead,
    DateTime CreatedAt);
