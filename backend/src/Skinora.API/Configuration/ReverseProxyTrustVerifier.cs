using System.Net;
using Microsoft.Extensions.Options;
using Skinora.Auth.Application.SteamAuthentication;

namespace Skinora.API.Configuration;

/// <summary>
/// T166 — <see cref="ITrustedProxyVerifier"/> backed by the SAME
/// <see cref="ReverseProxySettings"/> list that decides whether
/// <c>X-Forwarded-For</c> is honoured (F3a). The decision reads the direct
/// socket peer captured by <see cref="DirectPeer"/> before forwarded headers
/// are applied, so nothing the client sends — <c>X-Forwarded-For</c>,
/// <c>X-Real-IP</c>, <c>CF-Connecting-IP</c> — can make a request look as if
/// it came through the proxy.
/// </summary>
/// <remarks>
/// Not configured (no KnownNetworks/KnownProxies) → never trusted: the same
/// "missing information, not misplaced trust" posture as
/// <see cref="ReverseProxySettings"/>. No captured peer → not trusted.
/// </remarks>
public sealed class ReverseProxyTrustVerifier : ITrustedProxyVerifier
{
    private readonly IReadOnlyList<IPAddress> _proxies;
    private readonly IReadOnlyList<System.Net.IPNetwork> _networks;

    public ReverseProxyTrustVerifier(ReverseProxySettings settings)
    {
        _proxies = settings.ParseProxies().Select(Normalize).ToList();
        _networks = settings.ParseNetworks()
            .Select(n => new System.Net.IPNetwork(Normalize(n.Prefix), n.Length))
            .ToList();
    }

    public ReverseProxyTrustVerifier(IOptions<ReverseProxySettings> options)
        : this(options.Value)
    {
    }

    public bool IsFromTrustedProxy(HttpContext httpContext)
    {
        var peer = DirectPeer.Get(httpContext);
        if (peer is null) return false;

        peer = Normalize(peer);

        foreach (var proxy in _proxies)
        {
            if (proxy.Equals(peer)) return true;
        }

        foreach (var network in _networks)
        {
            if (network.Contains(peer)) return true;
        }

        return false;
    }

    // Kestrel reports IPv4 peers on a dual-stack socket as ::ffff:a.b.c.d
    // (the F3a live probe saw ::ffff:172.20.0.5); compare in plain IPv4.
    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
