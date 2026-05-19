using Microsoft.AspNetCore.Http;
using Skinora.Auth.Application.SteamAuthentication;

namespace Skinora.Auth.Tests.Unit;

public class ChainedCountryResolverTests
{
    private sealed class StubResolver : ICountryResolver
    {
        public string? Result { get; set; }
        public int Calls { get; private set; }

        public string? ResolveCountry(HttpContext? httpContext, string? ipAddress)
        {
            Calls++;
            return Result;
        }
    }

    [Fact]
    public void ResolveCountry_FirstReturnsCode_DoesNotCallSubsequent()
    {
        var first = new StubResolver { Result = "TR" };
        var second = new StubResolver { Result = "US" };
        var chain = new ChainedCountryResolver(new ICountryResolver[] { first, second });

        var result = chain.ResolveCountry(new DefaultHttpContext(), "1.2.3.4");

        Assert.Equal("TR", result);
        Assert.Equal(1, first.Calls);
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public void ResolveCountry_FirstReturnsNull_FallsThrough()
    {
        var first = new StubResolver { Result = null };
        var second = new StubResolver { Result = "US" };
        var chain = new ChainedCountryResolver(new ICountryResolver[] { first, second });

        var result = chain.ResolveCountry(new DefaultHttpContext(), "1.2.3.4");

        Assert.Equal("US", result);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public void ResolveCountry_AllReturnNull_ReturnsNull()
    {
        var first = new StubResolver { Result = null };
        var second = new StubResolver { Result = null };
        var chain = new ChainedCountryResolver(new ICountryResolver[] { first, second });

        var result = chain.ResolveCountry(new DefaultHttpContext(), "1.2.3.4");

        Assert.Null(result);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public void ResolveCountry_FirstReturnsWhitespace_TreatsAsNullAndFallsThrough()
    {
        var first = new StubResolver { Result = "   " };
        var second = new StubResolver { Result = "DE" };
        var chain = new ChainedCountryResolver(new ICountryResolver[] { first, second });

        var result = chain.ResolveCountry(new DefaultHttpContext(), "1.2.3.4");

        Assert.Equal("DE", result);
    }

    [Fact]
    public void ResolveCountry_EmptyChain_ReturnsNull()
    {
        var chain = new ChainedCountryResolver(Array.Empty<ICountryResolver>());

        var result = chain.ResolveCountry(new DefaultHttpContext(), "1.2.3.4");

        Assert.Null(result);
    }
}
