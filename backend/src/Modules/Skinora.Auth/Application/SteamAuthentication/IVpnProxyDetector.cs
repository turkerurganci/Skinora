namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Supportive signal detector — T83 / 02 §21.1. Returns true when the IP
/// is flagged by an upstream list (MVP: Tor exit nodes). The auth pipeline
/// **never blocks** on this signal; the boolean is persisted to
/// <c>UserLoginLog.HasVpnSignal</c> for future fraud rules to consume.
/// Errors must NOT throw — implementations return false on transient
/// failure so a torproject outage does not break login.
/// </summary>
public interface IVpnProxyDetector
{
    Task<bool> IsVpnOrProxyAsync(string? ipAddress, CancellationToken cancellationToken);
}

/// <summary>
/// No-op default. Used in environments where VPN detection is disabled
/// (<c>VpnDetection__Enabled=false</c>) and in unit tests that don't
/// exercise the detector.
/// </summary>
public sealed class NoOpVpnProxyDetector : IVpnProxyDetector
{
    public Task<bool> IsVpnOrProxyAsync(string? ipAddress, CancellationToken cancellationToken)
        => Task.FromResult(false);
}
