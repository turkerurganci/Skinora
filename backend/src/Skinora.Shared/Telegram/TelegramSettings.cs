namespace Skinora.Shared.Telegram;

/// <summary>
/// Telegram bot configuration (T79 — 08 §5.1–§5.5). Bound from the
/// <c>Telegram</c> section of <c>appsettings.json</c> or the equivalent
/// <c>Telegram__*</c> environment variables.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Provider"/> switch picks between the production
/// Telegram Bot API transport and the development/test logging stub so
/// a misconfigured local environment cannot accidentally contact the
/// real bot. CI and integration tests leave <see cref="Provider"/> at
/// <c>logging</c>; production must set it to <c>telegram</c> and supply
/// <see cref="BotToken"/>, <see cref="BotUsername"/> and
/// <see cref="WebhookSecretToken"/>.
/// </para>
/// <para>
/// Secret values (<see cref="BotToken"/>, <see cref="WebhookSecretToken"/>)
/// must come from Docker Secrets / vault in production (05 §3.5); the
/// <c>REPLACE_IN_ENV</c> defaults in <c>appsettings.json</c> are a
/// deliberate trip-wire so an unconfigured deployment fails closed.
/// </para>
/// <para>
/// This single class consolidates the legacy
/// <c>Skinora.Users.Application.Settings.TelegramSettings</c> (T35
/// connection-side config) with the new transport + rate-limit knobs;
/// every consumer (WebhooksController, TelegramConnectionService,
/// TelegramBotClient, channel handler) reads from the same bound
/// instance.
/// </para>
/// </remarks>
public sealed class TelegramSettings
{
    public const string SectionName = "Telegram";

    public const string ProviderTelegram = "telegram";
    public const string ProviderLogging = "logging";

    /// <summary>
    /// Active provider — <c>telegram</c> wires the real HTTP transport,
    /// <c>logging</c> keeps the T37 stub channel handler. Defaults to
    /// <c>logging</c> so a fresh checkout never contacts Telegram.
    /// </summary>
    public string Provider { get; set; } = ProviderLogging;

    /// <summary>
    /// Bot token issued by <c>@BotFather</c> in the format
    /// <c>123456789:ABCdef...</c>. Required when <see cref="Provider"/>
    /// is <c>telegram</c>.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Public bot username (without the leading <c>@</c>) — used to
    /// build the deep-link URL <c>https://t.me/{BotUsername}?start={code}</c>.
    /// </summary>
    public string BotUsername { get; set; } = "SkinoraBot";

    /// <summary>
    /// Override of the deep-link URL surfaced to the user. Defaults to
    /// <see cref="BotUsername"/>-based <c>https://t.me/...</c>; provided
    /// so test/staging deployments can point at a different bot without
    /// having to know the deep-link convention.
    /// </summary>
    public string BotUrl { get; set; } = "https://t.me/SkinoraBot";

    /// <summary>
    /// Telegram Bot API base URL. Override only when targeting a regional
    /// endpoint or a local recording proxy. Default is the documented
    /// public endpoint.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.telegram.org";

    /// <summary>
    /// Per-request HTTP timeout. Telegram's <c>sendMessage</c> is
    /// sub-second in the happy path; a 10-second budget matches T78
    /// Resend and keeps the deferred-tier scheduling responsive without
    /// false-failing on flaky networks.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Shared secret echoed by Telegram in the
    /// <c>X-Telegram-Bot-Api-Secret-Token</c> header on every webhook
    /// request (08 §5.2). Required when <see cref="Provider"/> is
    /// <c>telegram</c> — missing value forces 401 on every inbound
    /// webhook.
    /// </summary>
    public string WebhookSecretToken { get; set; } = string.Empty;

    /// <summary>
    /// TTL of the connection code that the user pastes into
    /// <c>/start {code}</c>. Plan 08 §5.1 — 10 minutes (600 seconds).
    /// Floored to 60 seconds in the connection service to keep
    /// integration tests configurable.
    /// </summary>
    public int CodeTtlSeconds { get; set; } = 600;

    /// <summary>
    /// Maximum number of failed webhook verifications before the
    /// outstanding code is invalidated (08 §5.1 brute-force protection).
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// TTL of the processed <c>update_id</c> dedup window (08 §5.2 —
    /// 24 hours). Telegram retries failed deliveries; the webhook
    /// middleware drops duplicates inside this window.
    /// </summary>
    public int IdempotencyTtlHours { get; set; } = 24;

    /// <summary>
    /// Maximum messages per chat per second (08 §5.3 — 1 msg/s). The
    /// rate limiter blocks the calling thread until the per-chat budget
    /// is available.
    /// </summary>
    public int PerChatRatePerSecond { get; set; } = 1;

    /// <summary>
    /// Maximum messages globally per second (08 §5.3 — 30 msg/s).
    /// </summary>
    public int GlobalRatePerSecond { get; set; } = 30;
}
