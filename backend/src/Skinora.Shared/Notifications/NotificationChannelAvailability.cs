using System.Text.RegularExpressions;
using Skinora.Shared.Discord;
using Skinora.Shared.Email;
using Skinora.Shared.Enums;
using Skinora.Shared.Telegram;

namespace Skinora.Shared.Notifications;

/// <summary>
/// T165 — what an external notification channel (e-mail, Telegram, Discord)
/// actually does in this deployment, decided once at startup from its
/// <c>Provider</c> switch and its secrets.
/// </summary>
public enum NotificationChannelState
{
    /// <summary><c>Provider = logging</c> (development, CI): the stub logs the message and reports success.</summary>
    Stub,

    /// <summary>The real provider is selected and every secret it needs is present and well-formed.</summary>
    Live,

    /// <summary><c>Provider = disabled</c>: the channel is switched off on purpose (production Discord, owner decision 2026-10-21).</summary>
    Disabled,

    /// <summary>
    /// The real provider is selected but a secret is missing, still a placeholder or malformed.
    /// Treated exactly like <see cref="Disabled"/> — nothing is sent, the channel cannot be connected —
    /// and reported as an error (startup log + <c>skinora_backend_notification_channel_state</c>).
    /// </summary>
    Misconfigured,
}

/// <summary>One channel's evaluated state plus the configuration keys that kept it from being live.</summary>
public sealed record NotificationChannelStatus(
    NotificationChannel Channel,
    NotificationChannelState State,
    string Provider,
    IReadOnlyList<string> Problems)
{
    /// <summary>
    /// True when the platform may create deliveries on this channel and let a user connect it:
    /// the real provider with complete secrets, or the development stub. Never true for
    /// <see cref="NotificationChannelState.Disabled"/> / <see cref="NotificationChannelState.Misconfigured"/>.
    /// </summary>
    public bool IsAvailable => State is NotificationChannelState.Live or NotificationChannelState.Stub;

    /// <summary>True only when the real transport may be wired (HTTP client registered).</summary>
    public bool IsLive => State == NotificationChannelState.Live;
}

/// <summary>Read side used by the dispatcher and the settings endpoints.</summary>
public interface INotificationChannelAvailability
{
    NotificationChannelStatus Get(NotificationChannel channel);

    bool IsAvailable(NotificationChannel channel);

    IReadOnlyList<NotificationChannelStatus> All { get; }
}

/// <summary>
/// T165 — evaluates the three external channels from their bound settings.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not fail-fast.</b> The trial deployment selects the real providers
/// before the owner has the Resend account and the Telegram bot (the keys arrive
/// later). Refusing to start would take the whole platform down for a channel;
/// instead a channel whose secrets are incomplete is <see cref="NotificationChannelState.Misconfigured"/>:
/// it behaves as switched off (no delivery rows, connect endpoints answer
/// <c>CHANNEL_UNAVAILABLE</c>, the UI shows it unavailable) and is reported loudly —
/// an error log line naming the missing keys (never their values) and a
/// Prometheus series that the <c>notification-channel-misconfigured</c> alert reads.
/// Filling the secrets and restarting the backend makes it <see cref="NotificationChannelState.Live"/>.
/// </para>
/// <para>
/// <b>Why the format checks.</b> <c>.env.example</c> ships placeholders
/// (<c>your_telegram_bot_token_here</c>) that Grafana needs to boot; a placeholder
/// that reached the real transport would look configured and fail on every send.
/// Each check is the documented shape of the secret (Resend API key <c>re_…</c>,
/// Svix signing secret <c>whsec_…</c>, BotFather token <c>&lt;digits&gt;:&lt;35 chars&gt;</c>,
/// Telegram webhook secret 1–256 of <c>A–Z a–z 0–9 _ -</c>).
/// </para>
/// </remarks>
public sealed class NotificationChannelAvailability : INotificationChannelAvailability
{
    /// <summary>Provider value that switches a channel off on purpose (all three channels).</summary>
    public const string ProviderDisabled = "disabled";

    private static readonly Regex TelegramBotTokenPattern =
        new(@"^\d{5,}:[A-Za-z0-9_-]{30,}$", RegexOptions.CultureInvariant);

    private static readonly Regex TelegramWebhookSecretPattern =
        new(@"^[A-Za-z0-9_-]{1,256}$", RegexOptions.CultureInvariant);

    private static readonly Regex TelegramUsernamePattern =
        new(@"^[A-Za-z][A-Za-z0-9_]{3,31}$", RegexOptions.CultureInvariant);

    private readonly IReadOnlyDictionary<NotificationChannel, NotificationChannelStatus> _statuses;

    public NotificationChannelAvailability(
        ResendSettings resend,
        TelegramSettings telegram,
        DiscordSettings discord)
    {
        var all = new[]
        {
            EvaluateEmail(resend),
            EvaluateTelegram(telegram),
            EvaluateDiscord(discord),
        };
        All = all;
        _statuses = all.ToDictionary(s => s.Channel);
    }

    /// <summary>
    /// Evaluates straight from configuration keys (<c>Resend:ApiKey</c>, …) so every
    /// composition site — Program.cs, the Notifications and Users modules — reaches
    /// the same decision before the container is built. <paramref name="read"/> is
    /// <c>key =&gt; configuration[key]</c>; an absent key keeps the settings default.
    /// </summary>
    public static NotificationChannelAvailability FromConfiguration(Func<string, string?> read)
    {
        var resend = new ResendSettings();
        Fill(resend, ResendSettings.SectionName, read,
            nameof(ResendSettings.Provider), nameof(ResendSettings.ApiKey),
            nameof(ResendSettings.WebhookSigningSecret), nameof(ResendSettings.FromAddress));

        var telegram = new TelegramSettings();
        Fill(telegram, TelegramSettings.SectionName, read,
            nameof(TelegramSettings.Provider), nameof(TelegramSettings.BotToken),
            nameof(TelegramSettings.WebhookSecretToken), nameof(TelegramSettings.BotUsername),
            nameof(TelegramSettings.BotUrl));

        var discord = new DiscordSettings();
        Fill(discord, DiscordSettings.SectionName, read,
            nameof(DiscordSettings.Provider), nameof(DiscordSettings.ClientId),
            nameof(DiscordSettings.ClientSecret), nameof(DiscordSettings.BotToken),
            nameof(DiscordSettings.RedirectUri));

        return new NotificationChannelAvailability(resend, telegram, discord);
    }

    /// <summary>Copies <c>{section}:{property}</c> onto the string property when the key is present.</summary>
    private static void Fill(object target, string section, Func<string, string?> read, params string[] properties)
    {
        foreach (var name in properties)
        {
            var value = read($"{section}:{name}");
            if (value is not null)
            {
                target.GetType().GetProperty(name)!.SetValue(target, value);
            }
        }
    }

    public IReadOnlyList<NotificationChannelStatus> All { get; }

    public NotificationChannelStatus Get(NotificationChannel channel) => _statuses[channel];

    public bool IsAvailable(NotificationChannel channel) => Get(channel).IsAvailable;

    /// <summary>E-mail through Resend (08 §4).</summary>
    public static NotificationChannelStatus EvaluateEmail(ResendSettings settings)
    {
        var provider = Normalize(settings.Provider, ResendSettings.ProviderLogging);
        if (provider == ResendSettings.ProviderLogging)
            return new(NotificationChannel.EMAIL, NotificationChannelState.Stub, provider, []);
        if (provider == ProviderDisabled)
            return new(NotificationChannel.EMAIL, NotificationChannelState.Disabled, provider, []);
        if (provider != ResendSettings.ProviderResend)
            return Unknown(NotificationChannel.EMAIL, provider, "Resend:Provider");

        var problems = new List<string>();
        Require(problems, "Resend:ApiKey", settings.ApiKey, v => v.StartsWith("re_", StringComparison.Ordinal) && v.Length > 10);
        Require(problems, "Resend:WebhookSigningSecret", settings.WebhookSigningSecret,
            v => v.StartsWith("whsec_", StringComparison.Ordinal) && v.Length > 10);
        Require(problems, "Resend:FromAddress", settings.FromAddress, v => v.Contains('@'));
        return Result(NotificationChannel.EMAIL, provider, problems);
    }

    /// <summary>Telegram bot (08 §5).</summary>
    public static NotificationChannelStatus EvaluateTelegram(TelegramSettings settings)
    {
        var provider = Normalize(settings.Provider, TelegramSettings.ProviderLogging);
        if (provider == TelegramSettings.ProviderLogging)
            return new(NotificationChannel.TELEGRAM, NotificationChannelState.Stub, provider, []);
        if (provider == ProviderDisabled)
            return new(NotificationChannel.TELEGRAM, NotificationChannelState.Disabled, provider, []);
        if (provider != TelegramSettings.ProviderTelegram)
            return Unknown(NotificationChannel.TELEGRAM, provider, "Telegram:Provider");

        var problems = new List<string>();
        Require(problems, "Telegram:BotToken", settings.BotToken, TelegramBotTokenPattern.IsMatch);
        Require(problems, "Telegram:WebhookSecretToken", settings.WebhookSecretToken, TelegramWebhookSecretPattern.IsMatch);
        Require(problems, "Telegram:BotUsername", settings.BotUsername, TelegramUsernamePattern.IsMatch);
        Require(problems, "Telegram:BotUrl", settings.BotUrl,
            v => v.StartsWith("https://t.me/", StringComparison.OrdinalIgnoreCase));
        return Result(NotificationChannel.TELEGRAM, provider, problems);
    }

    /// <summary>Discord bot + OAuth (08 §6). Production keeps it <c>disabled</c>.</summary>
    public static NotificationChannelStatus EvaluateDiscord(DiscordSettings settings)
    {
        var provider = Normalize(settings.Provider, DiscordSettings.ProviderLogging);
        if (provider == DiscordSettings.ProviderLogging)
            return new(NotificationChannel.DISCORD, NotificationChannelState.Stub, provider, []);
        if (provider == ProviderDisabled)
            return new(NotificationChannel.DISCORD, NotificationChannelState.Disabled, provider, []);
        if (provider != DiscordSettings.ProviderDiscord)
            return Unknown(NotificationChannel.DISCORD, provider, "Discord:Provider");

        var problems = new List<string>();
        Require(problems, "Discord:ClientId", settings.ClientId, v => v.All(char.IsAsciiDigit));
        Require(problems, "Discord:ClientSecret", settings.ClientSecret, _ => true);
        Require(problems, "Discord:BotToken", settings.BotToken, _ => true);
        Require(problems, "Discord:RedirectUri", settings.RedirectUri,
            v => v.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        return Result(NotificationChannel.DISCORD, provider, problems);
    }

    /// <summary>
    /// True for a value an operator has not filled: empty, the appsettings trip-wire
    /// <c>REPLACE_IN_ENV</c>, or an <c>.env.example</c> placeholder (<c>your_…</c>).
    /// </summary>
    public static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = value.Trim();
        return v.Equals("REPLACE_IN_ENV", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("your_", StringComparison.OrdinalIgnoreCase)
            || v.Equals("changeme", StringComparison.OrdinalIgnoreCase);
    }

    private static void Require(List<string> problems, string key, string? value, Func<string, bool> wellFormed)
    {
        if (IsPlaceholder(value))
        {
            problems.Add($"{key} (missing)");
            return;
        }

        if (!wellFormed(value!.Trim()))
        {
            problems.Add($"{key} (malformed)");
        }
    }

    private static string Normalize(string? provider, string fallback) =>
        string.IsNullOrWhiteSpace(provider) ? fallback : provider.Trim().ToLowerInvariant();

    private static NotificationChannelStatus Unknown(NotificationChannel channel, string provider, string key) =>
        new(channel, NotificationChannelState.Misconfigured, provider, [$"{key} (unknown value)"]);

    private static NotificationChannelStatus Result(
        NotificationChannel channel, string provider, List<string> problems) =>
        new(channel,
            problems.Count == 0 ? NotificationChannelState.Live : NotificationChannelState.Misconfigured,
            provider,
            problems);
}
