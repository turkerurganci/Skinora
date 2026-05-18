namespace Skinora.Shared.Discord;

/// <summary>
/// Discord bot + OAuth2 configuration (T80 — 08 §6.1–§6.5). Bound from
/// the <c>Discord</c> section of <c>appsettings.json</c> or the
/// equivalent <c>Discord__*</c> environment variables.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Provider"/> switch picks between the production
/// Discord API transport and the development/test logging/stub clients
/// so a misconfigured local environment cannot accidentally contact
/// Discord. CI and integration tests leave <see cref="Provider"/> at
/// <c>logging</c>; production must set it to <c>discord</c> and supply
/// <see cref="ClientId"/>, <see cref="ClientSecret"/> and
/// <see cref="BotToken"/>.
/// </para>
/// <para>
/// Secret values (<see cref="ClientSecret"/>, <see cref="BotToken"/>)
/// must come from Docker Secrets / vault in production (05 §3.5); the
/// <c>REPLACE_IN_ENV</c> defaults in <c>appsettings.json</c> are a
/// deliberate trip-wire so an unconfigured deployment fails closed.
/// </para>
/// <para>
/// This single class consolidates the legacy
/// <c>Skinora.Users.Application.Settings.DiscordSettings</c> (T35
/// OAuth-only config) with the new bot transport + DM cache knobs;
/// every consumer (UsersController callback, DiscordConnectionService,
/// DiscordOAuthClient, DiscordBotClient, channel handler) reads from
/// the same bound instance.
/// </para>
/// </remarks>
public sealed class DiscordSettings
{
    public const string SectionName = "Discord";

    public const string ProviderDiscord = "discord";
    public const string ProviderLogging = "logging";

    /// <summary>
    /// Active provider — <c>discord</c> wires the real HTTP transport,
    /// <c>logging</c> keeps the T37 stub channel handler + T35 stub
    /// OAuth client. Defaults to <c>logging</c> so a fresh checkout
    /// never contacts Discord.
    /// </summary>
    public string Provider { get; set; } = ProviderLogging;

    /// <summary>
    /// Discord application client ID — issued by the Developer Portal.
    /// Required for the OAuth2 authorize + token endpoints.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Discord application client secret — issued by the Developer
    /// Portal. Required for the OAuth2 token endpoint
    /// (<c>application/x-www-form-urlencoded</c> body).
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Bot token issued by the Developer Portal. Required when
    /// <see cref="Provider"/> is <c>discord</c>; embedded in the
    /// <c>Authorization: Bot {token}</c> header on every bot-scope API
    /// call (createDM, sendMessage).
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 authorize URL. Defaults to the documented public endpoint;
    /// override only for staging mirrors.
    /// </summary>
    public string AuthorizeUrl { get; set; } = "https://discord.com/api/oauth2/authorize";

    /// <summary>
    /// Discord API base URL (v10). The OAuth2 token endpoint and the
    /// bot-scope endpoints (users/@me, users/@me/channels,
    /// channels/{id}/messages) all live under this prefix.
    /// </summary>
    public string BaseUrl { get; set; } = "https://discord.com/api/v10";

    /// <summary>
    /// OAuth2 callback URL — must exactly match the redirect_uri
    /// configured in the Developer Portal application settings.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 scope — 08 §6.1 mandates the minimum <c>identify</c>
    /// scope (kullanıcı kimliği bağlama için yeterli).
    /// </summary>
    public string Scope { get; set; } = "identify";

    /// <summary>
    /// TTL of the OAuth2 state token (CSRF correlation, 07 §5.13).
    /// Floored to 60s in the connection service.
    /// </summary>
    public int StateTtlSeconds { get; set; } = 600;

    /// <summary>
    /// Frontend redirect on successful binding.
    /// </summary>
    public string SuccessRedirectUrl { get; set; } = "/settings?discord=connected";

    /// <summary>
    /// Frontend redirect prefix for callback failures — the connection
    /// service appends <c>&amp;reason=...</c> per 08 §6.4.
    /// </summary>
    public string FailureRedirectUrl { get; set; } = "/settings?discord=error";

    /// <summary>
    /// Per-request HTTP timeout. Discord's createDM + sendMessage are
    /// sub-second in the happy path; a 10-second budget mirrors T78
    /// Resend and T79 Telegram and keeps the deferred-tier scheduling
    /// responsive without false-failing on flaky networks.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Global throughput cap across all Discord bot-scope calls
    /// (08 §6.3 — ~50 req/s typical). Discord enforces per-bucket
    /// limits via response headers; this global ceiling protects the
    /// platform from a runaway loop hammering the API regardless of
    /// bucket. Defaults to 45 to leave a small margin under the typical
    /// 50 req/s global cap.
    /// </summary>
    public int GlobalRatePerSecond { get; set; } = 45;

    /// <summary>
    /// DM channel id cache TTL (08 §6.3 — "DM channel ID cache:
    /// Redis"). Discord channels persist for the lifetime of the
    /// bot/user relationship, but Redis keys are bounded for memory
    /// hygiene and to recover from rare server-side channel rotations.
    /// </summary>
    public int DmChannelCacheTtlHours { get; set; } = 24;

    /// <summary>
    /// Number of immediate retries when Discord returns a 5xx /
    /// transport error (08 §6.4 — "5xx → 3 deneme"). The notification
    /// delivery pipeline owns the actual backoff schedule
    /// (1 dk / 5 dk / 15 dk); this knob exists for the test harness so
    /// retry behaviour can be exercised deterministically.
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}
