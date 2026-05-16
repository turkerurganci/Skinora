namespace Skinora.API.Middleware;

/// <summary>
/// Inbound webhook signing parameters (05 §3.4, 09 §11.3).
/// Bound from <c>appsettings.json</c> "Webhook" section or environment
/// (e.g. <c>Webhook__SteamSharedSecret</c>). The Steam sidecar reads the same
/// secret from its <c>WEBHOOK_SECRET</c> env var (09 §17.5).
/// </summary>
public sealed class WebhookSettings
{
    public const string SectionName = "Webhook";

    /// <summary>
    /// HMAC-SHA256 shared secret used to verify <c>X-Signature</c> on requests
    /// from the Steam sidecar. Must be a non-empty random string in production;
    /// requests are rejected with 401 if the value is blank.
    /// </summary>
    public string SteamSharedSecret { get; set; } = string.Empty;

    /// <summary>
    /// HMAC-SHA256 shared secret used to verify <c>X-Signature</c> on requests
    /// from the blockchain sidecar (T71). Must be a non-empty random string in
    /// production; requests are rejected with 401 if the value is blank. The
    /// blockchain sidecar reads the same secret from its <c>WEBHOOK_SECRET</c>
    /// env var. May share a value with <see cref="SteamSharedSecret"/> in
    /// single-tenant deployments, but keeping it separate lets operators
    /// rotate sidecar credentials independently.
    /// </summary>
    public string BlockchainSharedSecret { get; set; } = string.Empty;

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
}
