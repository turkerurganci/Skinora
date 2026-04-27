using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Resources;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Application.Templates;

/// <summary>
/// Default <see cref="INotificationTemplateResolver"/> backed by
/// <see cref="IStringLocalizer{NotificationTemplates}"/> over the embedded
/// <c>NotificationTemplates.&lt;culture&gt;.resx</c> resource family
/// (05 §7.3).
/// </summary>
/// <remarks>
/// <para>
/// Resource lookup follows the standard .NET <see cref="ResourceManager"/>
/// culture chain: <c>tr-TR</c> falls back to <c>tr</c>, then to the neutral
/// <c>NotificationTemplates.resx</c> (English, 05 §7.3 fallback rule). Keys
/// follow the <c>{NotificationType}_Title</c> / <c>{NotificationType}_Body</c>
/// convention.
/// </para>
/// <para>
/// Placeholder substitution is intentionally permissive: missing keys in the
/// supplied parameter map produce literal <c>{Key}</c> output rather than
/// throwing, so a partially populated event payload still produces a usable
/// notification (05 §7.3 placeholder semantiği). Final/polished message
/// content is post-MVP per <c>MVP-OUT-016</c>.
/// </para>
/// </remarks>
public sealed class ResxNotificationTemplateResolver : INotificationTemplateResolver
{
    private readonly IStringLocalizer<NotificationTemplates> _localizer;
    private readonly ILogger<ResxNotificationTemplateResolver> _logger;

    public ResxNotificationTemplateResolver(
        IStringLocalizer<NotificationTemplates> localizer,
        ILogger<ResxNotificationTemplateResolver> logger)
    {
        _localizer = localizer;
        _logger = logger;
    }

    public RenderedNotificationTemplate Resolve(
        NotificationType type,
        string locale,
        IReadOnlyDictionary<string, string> parameters)
    {
        var culture = ResolveCulture(locale);

        var titleTemplate = LookupOrFallback(type, "Title", culture);
        var bodyTemplate = LookupOrFallback(type, "Body", culture);

        return new RenderedNotificationTemplate(
            Title: ApplyParameters(titleTemplate, parameters),
            Body: ApplyParameters(bodyTemplate, parameters));
    }

    private static CultureInfo ResolveCulture(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
            return CultureInfo.InvariantCulture;

        try
        {
            return CultureInfo.GetCultureInfo(locale);
        }
        catch (CultureNotFoundException)
        {
            // Unknown culture string — fall back to invariant which makes the
            // localizer return the neutral NotificationTemplates.resx entries.
            return CultureInfo.InvariantCulture;
        }
    }

    private string LookupOrFallback(NotificationType type, string suffix, CultureInfo culture)
    {
        var key = $"{type}_{suffix}";

        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;
        try
        {
            var localized = _localizer[key];
            if (!localized.ResourceNotFound)
                return localized.Value;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        _logger.LogWarning(
            "Notification template key {Key} missing for culture {Culture}; using key name as placeholder.",
            key,
            culture.Name);

        return key;
    }

    private static string ApplyParameters(string template, IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.Count == 0)
            return template;

        var rendered = template;
        foreach (var pair in parameters)
        {
            rendered = rendered.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        }

        return rendered;
    }
}
