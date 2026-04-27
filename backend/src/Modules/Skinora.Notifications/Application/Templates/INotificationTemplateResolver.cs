using Skinora.Shared.Enums;

namespace Skinora.Notifications.Application.Templates;

/// <summary>
/// Resolves a <see cref="NotificationType"/> + locale + parameter map into a
/// rendered <see cref="RenderedNotificationTemplate"/>. Locale fallback is
/// English when the requested culture has no resource entry (05 §7.3).
/// </summary>
public interface INotificationTemplateResolver
{
    RenderedNotificationTemplate Resolve(
        NotificationType type,
        string locale,
        IReadOnlyDictionary<string, string> parameters);
}
