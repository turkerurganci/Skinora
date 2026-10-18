using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Skinora.API.Middleware;

/// <summary>
/// Inbound webhook signing parameters (05 §3.4, 09 §11.3).
/// Bound from <c>appsettings.json</c> "Webhook" section or environment
/// (e.g. <c>Webhook__BlockchainSharedSecret</c>). The blockchain sidecar reads
/// the same secret from its <c>WEBHOOK_SECRET</c> env var (09 §17.5).
/// </summary>
/// <remarks>
/// <c>SteamSharedSecret</c> was removed with the bot custody layer in v3.0
/// (T132 — 02 §15): the backend serves no inbound Steam webhook, so there is
/// no Steam signature to verify. Steam is reached outbound-only, through the
/// read-only sidecar proxy.
/// </remarks>
public sealed class WebhookSettings
{
    public const string SectionName = "Webhook";

    /// <summary>
    /// HMAC-SHA256 shared secret used to verify <c>X-Signature</c> on requests
    /// from the blockchain sidecar (T71). Must be a non-empty random string in
    /// production; requests are rejected with 401 if the value is blank. The
    /// blockchain sidecar reads the same secret from its <c>WEBHOOK_SECRET</c>
    /// env var. Kept as a per-sidecar setting rather than a single global one
    /// so operators can rotate sidecar credentials independently.
    /// </summary>
    public string BlockchainSharedSecret { get; set; } = string.Empty;

    /// <summary>
    /// HMAC-SHA256 shared secret of the Ethereum service (T148 — 05 §3.3a
    /// "Kimlik doğrulama": every network's service has its own webhook secret,
    /// so one service's leaked secret cannot forge the other's callbacks).
    /// <see cref="BlockchainSharedSecret"/> is TRON's. Blank = no Ethereum
    /// service yet: no callback can authenticate as Ethereum.
    /// </summary>
    public string EthereumSharedSecret { get; set; } = string.Empty;

    /// <summary>
    /// Maximum allowed clock skew between sidecar and backend (05 §3.4).
    /// Default ±5 minutes per 09 §11.3.
    /// </summary>
    public int ReplayWindowSeconds { get; set; } = 300;

    /// <summary>
    /// How long an accepted nonce stays in <c>ProcessedNonces</c> before
    /// <c>ProcessedNonceCleanupJob</c> may purge it. Must be ≥
    /// <see cref="ReplayWindowSeconds"/> so a stale request cannot squeak past
    /// the replay window. Default 1 hour.
    /// </summary>
    public int NonceRetentionSeconds { get; set; } = 3600;

    /// <summary>
    /// Binds the section and refuses to start the host when two networks share
    /// one webhook secret (T148 düzeltme turu 1 — F3). A shared secret would
    /// let either service speak for the other, which is exactly what
    /// per-network secrets exist to prevent; silently keeping only TRON's (the
    /// pre-fix behaviour) left every Ethereum callback failing with 401 and
    /// nothing at startup saying why. Blank secrets are not compared — an
    /// unconfigured network has none.
    /// </summary>
    public static IServiceCollection AddWebhookSettings(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<WebhookSettings>()
            .Bind(configuration.GetSection(SectionName))
            .Validate(s => !NetworksShareASecret(s), SharedSecretConflictMessage)
            .ValidateOnStart();
        return services;
    }

    /// <summary>The startup error when two networks share one webhook secret.</summary>
    public const string SharedSecretConflictMessage =
        "Webhook:BlockchainSharedSecret (TRON) and Webhook:EthereumSharedSecret (ETHEREUM) are the same value. "
        + "Every network's service needs its own webhook secret (05 §3.3a, DEPLOY_RUNBOOK §B) — "
        + "set a different EthereumSharedSecret, or leave it empty until the Ethereum service exists.";

    /// <summary>True when TRON's and Ethereum's secrets are the same non-blank value.</summary>
    public static bool NetworksShareASecret(WebhookSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.BlockchainSharedSecret)
        && string.Equals(settings.BlockchainSharedSecret, settings.EthereumSharedSecret, StringComparison.Ordinal);
}
