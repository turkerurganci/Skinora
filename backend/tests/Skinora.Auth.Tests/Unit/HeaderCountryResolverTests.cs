using Microsoft.AspNetCore.Http;
using Skinora.Auth.Application.SteamAuthentication;

namespace Skinora.Auth.Tests.Unit;

public class HeaderCountryResolverTests
{
    private sealed class FixedTrust(bool trusted) : ITrustedProxyVerifier
    {
        public int Calls { get; private set; }

        public bool IsFromTrustedProxy(HttpContext httpContext)
        {
            Calls++;
            return trusted;
        }
    }

    private readonly HeaderCountryResolver _resolver = new(new FixedTrust(trusted: true));

    [Fact]
    public void ResolveCountry_NullHttpContext_ReturnsNull()
    {
        Assert.Null(_resolver.ResolveCountry(null, "1.2.3.4"));
    }

    [Fact]
    public void ResolveCountry_NoHeader_ReturnsNull()
    {
        var ctx = new DefaultHttpContext();
        Assert.Null(_resolver.ResolveCountry(ctx, "1.2.3.4"));
    }

    [Fact]
    public void ResolveCountry_ValidHeader_ReturnsUpperCaseCode()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Country-Code"] = "tr";

        Assert.Equal("TR", _resolver.ResolveCountry(ctx, null));
    }

    [Fact]
    public void ResolveCountry_MultiValueHeader_ReturnsFirstCode()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Country-Code"] = "US, CA";

        Assert.Equal("US", _resolver.ResolveCountry(ctx, null));
    }

    [Fact]
    public void ResolveCountry_InvalidLengthHeader_ReturnsNull()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Country-Code"] = "USA";

        Assert.Null(_resolver.ResolveCountry(ctx, null));
    }

    [Fact]
    public void ResolveCountry_EmptyHeader_ReturnsNull()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Country-Code"] = "";

        Assert.Null(_resolver.ResolveCountry(ctx, null));
    }

    // --- T166 — the header is believed only from our own proxy ---

    [Fact]
    public void ResolveCountry_HeaderFromUntrustedPeer_IsIgnored()
    {
        // The bypass this closes: a client in a banned country sends
        // X-Country-Code itself. Before T166 the header won over MaxMind.
        var resolver = new HeaderCountryResolver(new FixedTrust(trusted: false));
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Country-Code"] = "US";

        Assert.Null(resolver.ResolveCountry(ctx, "5.6.7.8"));
    }

    [Fact]
    public void ResolveCountry_NoHeader_DoesNotConsultTrust()
    {
        var trust = new FixedTrust(trusted: true);
        var resolver = new HeaderCountryResolver(trust);

        Assert.Null(resolver.ResolveCountry(new DefaultHttpContext(), null));
        Assert.Equal(0, trust.Calls);
    }

    [Fact]
    public void ResolveCountry_CloudflareUnknownXX_ReturnsNull_SoFallbackRuns()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Country-Code"] = "XX";

        Assert.Null(_resolver.ResolveCountry(ctx, null));
    }

    [Fact]
    public void ResolveCountry_CloudflareTorT1_IsPassedThrough()
    {
        // T1 stays a code so an admin can ban it via auth.banned_countries.
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Country-Code"] = "T1";

        Assert.Equal("T1", _resolver.ResolveCountry(ctx, null));
    }
}
