namespace Skinora.Steam.Application.Inventory;

/// <summary>
/// Binding target for the <c>SteamSidecar</c> configuration section. Holds
/// the sidecar HTTP base URL and the shared internal-key used by the
/// <c>X-Internal-Key</c> header (05 §3.4 — service-to-service auth).
/// </summary>
public sealed class SteamSidecarOptions
{
    public const string SectionName = "SteamSidecar";

    /// <summary>
    /// Base URL of the Steam sidecar (e.g. <c>http://skinora-steam-sidecar:5100</c>).
    /// Trailing slashes are tolerated by <see cref="HttpClient"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Pre-shared key sent in the <c>X-Internal-Key</c> header on every
    /// outbound call. Mirrors <c>INTERNAL_KEY</c> on the sidecar (see
    /// <c>sidecar-steam/src/api/middleware.ts</c>).
    /// </summary>
    public string InternalKey { get; set; } = string.Empty;

    /// <summary>
    /// Per-request HTTP timeout in seconds. Inventory fetches over the
    /// Steam Community endpoint can pause when paginating large inventories
    /// (5000+ items × 1000 per page = 5 sequential HTTP round-trips), so the
    /// default is generous compared to the 2-second average response time.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
