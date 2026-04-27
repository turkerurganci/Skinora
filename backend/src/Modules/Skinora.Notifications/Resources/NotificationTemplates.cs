namespace Skinora.Notifications.Resources;

/// <summary>
/// Marker type used by <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/>
/// to locate the embedded <c>NotificationTemplates.resx</c> family in this
/// assembly. Microsoft.Extensions.Localization resolves resource files by
/// matching the marker type's full name (namespace + name) to the embedded
/// resource path, so this class lives next to the .resx files.
/// </summary>
/// <remarks>
/// Adding a new locale is a no-code change: drop a sibling
/// <c>NotificationTemplates.&lt;culture&gt;.resx</c> beside the neutral
/// <c>NotificationTemplates.resx</c>; <see cref="Templates.ResxNotificationTemplateResolver"/>
/// resolves it via the configured <c>CultureInfo</c> chain at request time.
/// </remarks>
public sealed class NotificationTemplates
{
}
