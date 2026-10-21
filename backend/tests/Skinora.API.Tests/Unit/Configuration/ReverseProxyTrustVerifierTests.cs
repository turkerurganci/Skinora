using System.Net;
using Microsoft.AspNetCore.Http;
using Skinora.API.Configuration;

namespace Skinora.API.Tests.Unit.Configuration;

/// <summary>
/// T166 — "did this request come through our reverse proxy?" is decided by the
/// direct socket peer only, against the ReverseProxy trust list.
/// </summary>
public class ReverseProxyTrustVerifierTests
{
    private static ReverseProxyTrustVerifier Verifier(string[]? networks = null, string[]? proxies = null) =>
        new(new ReverseProxySettings
        {
            KnownNetworks = networks ?? [],
            KnownProxies = proxies ?? [],
        });

    private static HttpContext From(string? peer, Action<HttpContext>? configure = null)
    {
        var ctx = new DefaultHttpContext();
        DirectPeer.Set(ctx, peer is null ? null : IPAddress.Parse(peer));
        configure?.Invoke(ctx);
        return ctx;
    }

    [Fact]
    public void NotConfigured_NeverTrusted()
    {
        Assert.False(Verifier().IsFromTrustedProxy(From("172.20.0.5")));
    }

    [Fact]
    public void PeerInsideKnownNetwork_Trusted()
    {
        Assert.True(Verifier(networks: ["172.20.0.0/16"]).IsFromTrustedProxy(From("172.20.0.5")));
    }

    [Fact]
    public void PeerOutsideKnownNetwork_NotTrusted()
    {
        Assert.False(Verifier(networks: ["172.20.0.0/16"]).IsFromTrustedProxy(From("203.0.113.7")));
    }

    [Fact]
    public void Ipv4MappedPeer_ComparedAsIpv4()
    {
        // Kestrel on a dual-stack socket reports ::ffff:a.b.c.d — F3a's live
        // probe recorded exactly ::ffff:172.20.0.5 for nginx.
        Assert.True(Verifier(networks: ["172.20.0.0/16"]).IsFromTrustedProxy(From("::ffff:172.20.0.5")));
    }

    [Fact]
    public void KnownProxyExactMatch_Trusted()
    {
        Assert.True(Verifier(proxies: ["10.1.2.3"]).IsFromTrustedProxy(From("10.1.2.3")));
        Assert.False(Verifier(proxies: ["10.1.2.3"]).IsFromTrustedProxy(From("10.1.2.4")));
    }

    [Fact]
    public void NoCapturedPeer_NotTrusted()
    {
        Assert.False(Verifier(networks: ["0.0.0.0/0"]).IsFromTrustedProxy(new DefaultHttpContext()));
    }

    [Fact]
    public void ClientHeadersCannotForgeTrust()
    {
        // The direct peer is the attacker; nothing it writes into forwarding
        // headers — nor what UseForwardedHeaders later puts into
        // RemoteIpAddress — may change the answer.
        var ctx = From("203.0.113.7", c =>
        {
            c.Request.Headers["X-Forwarded-For"] = "172.20.0.5";
            c.Request.Headers["X-Real-IP"] = "172.20.0.5";
            c.Request.Headers["CF-Connecting-IP"] = "172.20.0.5";
            c.Connection.RemoteIpAddress = IPAddress.Parse("172.20.0.5");
        });

        Assert.False(Verifier(networks: ["172.20.0.0/16"]).IsFromTrustedProxy(ctx));
    }
}
