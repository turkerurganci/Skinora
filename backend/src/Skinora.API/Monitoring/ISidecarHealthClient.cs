namespace Skinora.API.Monitoring;

/// <summary>
/// Probes a sidecar's <c>/health</c> liveness endpoint for the WP16 platform
/// health monitor.
/// </summary>
public interface ISidecarHealthClient
{
    /// <summary>
    /// Returns <c>true</c> when the component's sidecar answers <c>/health</c>
    /// with a success status, <c>false</c> when it errors / times out / returns
    /// non-success, and <c>null</c> when the component is not configured (no
    /// base URL) and therefore not monitored.
    /// </summary>
    Task<bool?> IsHealthyAsync(string component, CancellationToken cancellationToken);
}
