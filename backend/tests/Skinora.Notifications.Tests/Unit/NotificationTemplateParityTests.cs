using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Skinora.Notifications.Resources;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// Parity guard between <see cref="NotificationType"/> and the
/// <c>NotificationTemplates.&lt;culture&gt;.resx</c> family (05 §7.3, WP17
/// four-language parity).
/// </summary>
/// <remarks>
/// <para>
/// Why this exists (T118): the v3.0 P2P pivot renamed two notification types
/// (<c>ITEM_ESCROWED → PAYMENT_WINDOW_OPEN</c>,
/// <c>TRADE_OFFER_SENT_TO_BUYER → DELIVERY_EXPECTED</c>) but the resource keys
/// kept their old names in all four languages. Nothing failed:
/// <see cref="Application.Templates.ResxNotificationTemplateResolver"/> logs a
/// warning and returns the key name itself, so the two central P2P
/// notifications would have shipped rendering as the literal strings
/// "PAYMENT_WINDOW_OPEN_Title" / "DELIVERY_EXPECTED_Body" — the buyer never
/// learning the deposit address, the seller never being told to send the item.
/// A resolver that degrades instead of throwing needs a test at the catalogue
/// level; per-key tests only cover the keys someone remembered to add.
/// </para>
/// <para>
/// Both directions are asserted. Missing keys are the failure above; orphan
/// keys are the residue a rename leaves behind, and they are what makes the
/// next reader believe a retired type still exists.
/// </para>
/// </remarks>
public class NotificationTemplateParityTests
{
    /// <summary>Empty string = the neutral (English) resx; 05 §7.3 fallback root.</summary>
    public static TheoryData<string> Locales() => ["", "tr", "es", "zh"];

    [Theory]
    [MemberData(nameof(Locales))]
    [Trait("Category", "Unit")]
    public void EveryNotificationType_HasTitleAndBody_WithNoOrphans(string locale)
    {
        var expected = Enum.GetValues<NotificationType>()
            .SelectMany(type => new[] { $"{type}_Title", $"{type}_Body" })
            .ToHashSet(StringComparer.Ordinal);

        var actual = KeysFor(locale);

        // includeParentCultures: false — a key present only in the neutral resx
        // must not satisfy the tr/es/zh assertion. Per-key fallback still works
        // at runtime (05 §7.3), but WP17 established full four-language parity
        // and silently sliding back to English is a regression, not a feature.
        var missing = expected.Except(actual, StringComparer.Ordinal).Order().ToArray();
        var orphaned = actual.Except(expected, StringComparer.Ordinal).Order().ToArray();

        Assert.True(
            missing.Length == 0,
            $"Locale '{DescribeLocale(locale)}' has no template for: {string.Join(", ", missing)}");
        Assert.True(
            orphaned.Length == 0,
            $"Locale '{DescribeLocale(locale)}' has templates for retired keys: {string.Join(", ", orphaned)}");
    }

    private static HashSet<string> KeysFor(string locale)
    {
        var services = new ServiceCollection();
        services.AddLocalization();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();
        var localizer = provider.GetRequiredService<IStringLocalizer<NotificationTemplates>>();

        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = string.IsNullOrEmpty(locale)
            ? CultureInfo.InvariantCulture
            : CultureInfo.GetCultureInfo(locale);
        try
        {
            return localizer.GetAllStrings(includeParentCultures: false)
                .Select(entry => entry.Name)
                .ToHashSet(StringComparer.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static string DescribeLocale(string locale) =>
        string.IsNullOrEmpty(locale) ? "neutral (en)" : locale;
}
