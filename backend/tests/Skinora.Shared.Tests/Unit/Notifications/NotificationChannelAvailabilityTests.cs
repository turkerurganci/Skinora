using Skinora.Shared.Enums;
using Skinora.Shared.Notifications;

namespace Skinora.Shared.Tests.Unit.Notifications;

/// <summary>
/// T165 — channel state from Provider + secrets: the stub stays the default,
/// a real provider is live only with complete well-formed secrets, anything
/// else is Misconfigured (off + reported), "disabled" switches it off.
/// </summary>
public sealed class NotificationChannelAvailabilityTests
{
    private const string BotToken = "7012345678:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw0";
    private const string ResendKey = "re_123456789_abcdefghijklmnop";
    private const string ResendWebhook = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";

    private static NotificationChannelAvailability From(Dictionary<string, string?> config) =>
        NotificationChannelAvailability.FromConfiguration(key => config.GetValueOrDefault(key));

    [Fact]
    public void NothingConfigured_EveryChannelIsTheStub_AndAvailable()
    {
        var sut = From(new());

        Assert.All(sut.All, s =>
        {
            Assert.Equal(NotificationChannelState.Stub, s.State);
            Assert.True(s.IsAvailable);
            Assert.False(s.IsLive);
        });
    }

    [Fact]
    public void AppsettingsDefaults_WithLoggingProvider_AreTheStub()
    {
        // appsettings.json ships Provider=logging + REPLACE_IN_ENV trip-wires.
        var sut = From(new()
        {
            ["Resend:Provider"] = "logging",
            ["Resend:ApiKey"] = "REPLACE_IN_ENV",
            ["Telegram:Provider"] = "logging",
            ["Telegram:BotToken"] = "REPLACE_IN_ENV",
        });

        Assert.Equal(NotificationChannelState.Stub, sut.Get(NotificationChannel.EMAIL).State);
        Assert.Equal(NotificationChannelState.Stub, sut.Get(NotificationChannel.TELEGRAM).State);
    }

    [Fact]
    public void RealProviders_WithCompleteSecrets_AreLive()
    {
        var sut = From(new()
        {
            ["Resend:Provider"] = "resend",
            ["Resend:ApiKey"] = ResendKey,
            ["Resend:WebhookSigningSecret"] = ResendWebhook,
            ["Resend:FromAddress"] = "Skinora <noreply@getskinora.com>",
            ["Telegram:Provider"] = "Telegram", // case-insensitive
            ["Telegram:BotToken"] = BotToken,
            ["Telegram:WebhookSecretToken"] = "Zx9_kLm-0aBcDeFgHiJkLmNoPqRsTuVwXyZ",
            ["Telegram:BotUsername"] = "SkinoraAlertsBot",
            ["Telegram:BotUrl"] = "https://t.me/SkinoraAlertsBot",
            ["Discord:Provider"] = "disabled",
        });

        Assert.True(sut.Get(NotificationChannel.EMAIL).IsLive);
        Assert.True(sut.Get(NotificationChannel.TELEGRAM).IsLive);
        Assert.Equal(NotificationChannelState.Disabled, sut.Get(NotificationChannel.DISCORD).State);
        Assert.False(sut.IsAvailable(NotificationChannel.DISCORD));
    }

    [Fact]
    public void RealProviders_WithEmptySecrets_AreMisconfigured_AndNameEveryMissingKey()
    {
        // The trial overlay before the owner has the keys: provider forced,
        // secrets empty (compose passes "" for an unset variable).
        var sut = From(new()
        {
            ["Resend:Provider"] = "resend",
            ["Resend:ApiKey"] = "",
            ["Resend:WebhookSigningSecret"] = "",
            ["Telegram:Provider"] = "telegram",
            ["Telegram:BotToken"] = "",
            ["Telegram:WebhookSecretToken"] = "",
        });

        var email = sut.Get(NotificationChannel.EMAIL);
        Assert.Equal(NotificationChannelState.Misconfigured, email.State);
        Assert.False(email.IsAvailable);
        Assert.Equal(["Resend:ApiKey (missing)", "Resend:WebhookSigningSecret (missing)"], email.Problems);

        var telegram = sut.Get(NotificationChannel.TELEGRAM);
        Assert.Equal(NotificationChannelState.Misconfigured, telegram.State);
        Assert.Equal(["Telegram:BotToken (missing)", "Telegram:WebhookSecretToken (missing)"], telegram.Problems);
    }

    [Theory]
    // .env.example placeholders Grafana needs to boot must not look configured.
    [InlineData("your_telegram_bot_token_here", "Telegram:BotToken (missing)")]
    [InlineData("REPLACE_IN_ENV", "Telegram:BotToken (missing)")]
    [InlineData("123:short", "Telegram:BotToken (malformed)")]
    [InlineData("bot7012345678:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw0", "Telegram:BotToken (malformed)")]
    public void TelegramPlaceholderOrMalformedToken_IsMisconfigured(string token, string problem)
    {
        var sut = From(new()
        {
            ["Telegram:Provider"] = "telegram",
            ["Telegram:BotToken"] = token,
            ["Telegram:WebhookSecretToken"] = "abc",
        });

        Assert.Equal([problem], sut.Get(NotificationChannel.TELEGRAM).Problems);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has/slash")]
    public void TelegramWebhookSecret_OutsideTheAllowedCharacters_IsMalformed(string secret)
    {
        // setWebhook secret_token: 1–256 characters, A–Z a–z 0–9 _ - only.
        var sut = From(new()
        {
            ["Telegram:Provider"] = "telegram",
            ["Telegram:BotToken"] = BotToken,
            ["Telegram:WebhookSecretToken"] = secret,
        });

        Assert.Equal(["Telegram:WebhookSecretToken (malformed)"], sut.Get(NotificationChannel.TELEGRAM).Problems);
    }

    [Fact]
    public void ResendKeysWithoutTheirDocumentedPrefix_AreMalformed()
    {
        var sut = From(new()
        {
            ["Resend:Provider"] = "resend",
            ["Resend:ApiKey"] = "sk_live_something_long",
            ["Resend:WebhookSigningSecret"] = "MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw",
        });

        Assert.Equal(
            ["Resend:ApiKey (malformed)", "Resend:WebhookSigningSecret (malformed)"],
            sut.Get(NotificationChannel.EMAIL).Problems);
    }

    [Fact]
    public void UnknownProviderValue_IsMisconfigured_NotSilentlyTheStub()
    {
        var sut = From(new() { ["Resend:Provider"] = "smtp" });

        var email = sut.Get(NotificationChannel.EMAIL);
        Assert.Equal(NotificationChannelState.Misconfigured, email.State);
        Assert.Equal(["Resend:Provider (unknown value)"], email.Problems);
    }

    [Fact]
    public void DiscordReal_WithoutSecrets_IsMisconfigured()
    {
        var sut = From(new() { ["Discord:Provider"] = "discord" });

        Assert.Equal(NotificationChannelState.Misconfigured, sut.Get(NotificationChannel.DISCORD).State);
    }

    [Fact]
    public void Problems_NeverCarryTheSecretValue()
    {
        var sut = From(new()
        {
            ["Resend:Provider"] = "resend",
            ["Resend:ApiKey"] = "sk_this_is_the_secret_value",
        });

        Assert.All(sut.Get(NotificationChannel.EMAIL).Problems,
            p => Assert.DoesNotContain("this_is_the_secret_value", p));
    }
}
