namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Configuration for the Tor-exit-node VPN/proxy supportive signal (T83 /
/// 02 §21.1). The MVP scope is "supportive signal only — never blocks
/// login"; the detector enriches <c>UserLoginLog.HasVpnSignal</c> so
/// future fraud rules can consume the field. Disabled by default —
/// operator opts in via env var <c>VpnDetection__Enabled=true</c>.
/// </summary>
public sealed class VpnDetectionSettings
{
    public const string SectionName = "VpnDetection";

    /// <summary>
    /// Master switch. When false the pipeline wires
    /// <see cref="NoOpVpnProxyDetector"/> and skips the Tor list fetch.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Public Tor exit list endpoint. Override in tests / mirror sites.
    /// Default <c>https://check.torproject.org/torbulkexitlist</c>.
    /// </summary>
    public string TorExitListUrl { get; set; } = "https://check.torproject.org/torbulkexitlist";

    /// <summary>
    /// In-memory cache duration in minutes. Default 60 — torproject
    /// publishes the list with hourly cache headers.
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 60;

    /// <summary>
    /// HTTP timeout for the exit list refresh in seconds. Failures are
    /// soft — the detector returns <c>false</c> on error so login never
    /// blocks on network jitter.
    /// </summary>
    public int RefreshTimeoutSeconds { get; set; } = 10;

    public TimeSpan CacheDuration => TimeSpan.FromMinutes(Math.Max(1, CacheDurationMinutes));

    public TimeSpan RefreshTimeout => TimeSpan.FromSeconds(Math.Max(1, RefreshTimeoutSeconds));
}
