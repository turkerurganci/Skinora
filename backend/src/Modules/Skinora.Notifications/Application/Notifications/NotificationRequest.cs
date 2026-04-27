using Skinora.Shared.Enums;

namespace Skinora.Notifications.Application.Notifications;

/// <summary>
/// Input contract for <see cref="INotificationDispatcher.DispatchAsync"/> —
/// describes a single notification fan-out request that originates from a
/// MediatR domain event handler.
/// </summary>
/// <remarks>
/// The dispatcher resolves the user's locale from <see cref="Skinora.Users.Domain.Entities.User.PreferredLanguage"/>,
/// renders the template via <see cref="Templates.INotificationTemplateResolver"/>,
/// writes the platform-in-app <see cref="Domain.Entities.Notification"/> row,
/// then fans out to enabled external channels per
/// <see cref="Domain.Entities.UserNotificationPreference"/> (05 §7.2–§7.4).
/// </remarks>
public sealed class NotificationRequest
{
    public required Guid UserId { get; init; }

    public required NotificationType Type { get; init; }

    public Guid? TransactionId { get; init; }

    /// <summary>
    /// Template parameter map used by <see cref="Templates.INotificationTemplateResolver"/>
    /// to substitute <c>{Placeholder}</c> tokens in the resource string.
    /// Keys match the placeholder name without the braces; values are
    /// stringified verbatim. Missing keys yield literal placeholder text
    /// (no exception) so partially-filled templates remain safe (05 §7.3).
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
