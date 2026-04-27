using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Resources;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ResxNotificationTemplateResolver"/> covering
/// locale resolution, English fallback (05 §7.3) and parameter
/// substitution.
/// </summary>
public class ResxNotificationTemplateResolverTests
{
    private static ResxNotificationTemplateResolver CreateSut()
    {
        var services = new ServiceCollection();
        services.AddLocalization();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var localizer = provider.GetRequiredService<IStringLocalizer<NotificationTemplates>>();
        return new ResxNotificationTemplateResolver(
            localizer,
            NullLogger<ResxNotificationTemplateResolver>.Instance);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resolve_TurkishLocale_ReturnsTurkishStrings()
    {
        var sut = CreateSut();

        var rendered = sut.Resolve(
            NotificationType.TRANSACTION_INVITE,
            "tr",
            new Dictionary<string, string> { ["ItemName"] = "AK-47", ["Amount"] = "50" });

        Assert.Equal("Yeni işlem davetin var", rendered.Title);
        Assert.Equal("AK-47 için işlem daveti aldın (50 USDT).", rendered.Body);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resolve_ChineseLocale_ReturnsChineseStrings()
    {
        var sut = CreateSut();

        var rendered = sut.Resolve(
            NotificationType.PAYMENT_RECEIVED,
            "zh",
            new Dictionary<string, string> { ["Amount"] = "100" });

        Assert.Equal("已收到付款", rendered.Title);
        Assert.Equal("已收到您交易的 100 USDT。", rendered.Body);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resolve_LocaleMissingForKey_FallsBackToEnglish()
    {
        // Turkish .resx omits TRANSACTION_FLAGGED — resolver should return
        // the neutral English entry per 05 §7.3 fallback rule.
        var sut = CreateSut();

        var rendered = sut.Resolve(
            NotificationType.TRANSACTION_FLAGGED,
            "tr",
            new Dictionary<string, string> { ["TransactionId"] = "abc-123" });

        Assert.Equal("Transaction flagged", rendered.Title);
        Assert.Equal("Transaction abc-123 is under review.", rendered.Body);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resolve_UnknownLocaleString_FallsBackToInvariant()
    {
        var sut = CreateSut();

        var rendered = sut.Resolve(
            NotificationType.TRANSACTION_INVITE,
            "xx-XX",
            new Dictionary<string, string> { ["ItemName"] = "A", ["Amount"] = "1" });

        Assert.Equal("You have a new transaction invitation", rendered.Title);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resolve_MissingParameter_LeavesPlaceholderLiteral()
    {
        var sut = CreateSut();

        var rendered = sut.Resolve(
            NotificationType.TRANSACTION_INVITE,
            "en",
            new Dictionary<string, string> { ["ItemName"] = "AK-47" });

        // {Amount} is intentionally not in the parameter map; the resolver
        // is permissive and emits the literal placeholder rather than
        // throwing (05 §7.3).
        Assert.Contains("{Amount}", rendered.Body);
        Assert.Contains("AK-47", rendered.Body);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resolve_EmptyLocale_UsesInvariantNeutral()
    {
        var sut = CreateSut();

        var rendered = sut.Resolve(
            NotificationType.PAYMENT_RECEIVED,
            string.Empty,
            new Dictionary<string, string> { ["Amount"] = "10" });

        Assert.Equal("Payment received", rendered.Title);
    }
}
