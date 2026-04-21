using Microsoft.AspNetCore.Http;
using Skinora.Auth.Application.SteamAuthentication;

namespace Skinora.Auth.Tests.Unit;

public class HeaderCountryResolverTests
{
    private readonly HeaderCountryResolver _resolver = new();

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
}
