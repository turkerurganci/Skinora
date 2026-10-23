using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skinora.Notifications;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Infrastructure.Channels;

namespace Skinora.Notifications.Tests.Unit.Channels;

/// <summary>
/// T165 düzeltme turu (doğrulama bozması M25) — the module wires the real
/// Telegram handler only for a LIVE channel. A real provider with missing
/// secrets (misconfigured) keeps the logging stub; until this test, wiring the
/// real handler there went unnoticed (the dispatcher's skip hid it).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ChannelHandlerRegistrationTests
{
    private const string BotToken = "7012345678:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw0";

    [Fact]
    public void MisconfiguredTelegram_KeepsTheLoggingStub()
    {
        var handlers = TelegramHandlers(new Dictionary<string, string?>
        {
            ["Telegram:Provider"] = "telegram",
            ["Telegram:BotToken"] = "",
            ["Telegram:WebhookSecretToken"] = "",
        });

        Assert.Equal([typeof(LoggingTelegramNotificationChannelHandler)], handlers);
    }

    [Fact]
    public void LiveTelegram_WiresTheBotApiHandler_Contrast()
    {
        var handlers = TelegramHandlers(new Dictionary<string, string?>
        {
            ["Telegram:Provider"] = "telegram",
            ["Telegram:BotToken"] = BotToken,
            ["Telegram:WebhookSecretToken"] = "Zx9_kLm-0aBcDeFgHiJkLmNoPqRsTuVwXyZ",
            ["Telegram:BotUsername"] = "SkinoraAlertsBot",
            ["Telegram:BotUrl"] = "https://t.me/SkinoraAlertsBot",
        });

        Assert.Equal([typeof(TelegramNotificationChannelHandler)], handlers);
    }

    private static Type[] TelegramHandlers(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddNotificationsModule(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services
            .Where(d => d.ServiceType == typeof(INotificationChannelHandler))
            .Select(d => d.ImplementationType!)
            .Where(t => t.Name.Contains("Telegram", StringComparison.Ordinal))
            .ToArray();
    }
}
