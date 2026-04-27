namespace Skinora.Notifications.Application.Templates;

/// <summary>
/// Output of <see cref="INotificationTemplateResolver.Resolve"/> —
/// localized title and body strings ready for persistence and channel
/// dispatch (05 §7.3).
/// </summary>
public sealed record RenderedNotificationTemplate(string Title, string Body);
