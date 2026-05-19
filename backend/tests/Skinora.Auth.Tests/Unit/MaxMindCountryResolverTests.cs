using MaxMind.GeoIP2;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Auth.Application.SteamAuthentication;

namespace Skinora.Auth.Tests.Unit;

/// <summary>
/// Exercises <see cref="MaxMindCountryResolver"/> against the MaxMind
/// public test mmdb (Apache 2.0 — see <c>TestData/README.md</c>). Verifies
/// fail-open behaviour for invalid inputs and correct ISO normalization
/// for known IPs.
/// </summary>
public class MaxMindCountryResolverTests : IDisposable
{
    private static readonly string TestDbPath = Path.Combine(
        AppContext.BaseDirectory, "TestData", "GeoIP2-Country-Test.mmdb");

    private readonly DatabaseReader _reader;
    private readonly MaxMindCountryResolver _resolver;

    public MaxMindCountryResolverTests()
    {
        if (!File.Exists(TestDbPath))
            throw new FileNotFoundException($"Test mmdb missing: {TestDbPath}");
        _reader = new DatabaseReader(TestDbPath);
        _resolver = new MaxMindCountryResolver(_reader, NullLogger<MaxMindCountryResolver>.Instance);
    }

    public void Dispose() => _resolver.Dispose();

    [Fact]
    public void ResolveCountry_NullIp_ReturnsNull()
    {
        Assert.Null(_resolver.ResolveCountry(new DefaultHttpContext(), null));
    }

    [Fact]
    public void ResolveCountry_WhitespaceIp_ReturnsNull()
    {
        Assert.Null(_resolver.ResolveCountry(new DefaultHttpContext(), "   "));
    }

    [Fact]
    public void ResolveCountry_MalformedIp_ReturnsNull()
    {
        Assert.Null(_resolver.ResolveCountry(new DefaultHttpContext(), "not-an-ip"));
    }

    [Fact]
    public void ResolveCountry_PrivateIp_ReturnsNull()
    {
        // 10.0.0.1 is RFC1918 — not in the GeoIP2 country test db.
        Assert.Null(_resolver.ResolveCountry(new DefaultHttpContext(), "10.0.0.1"));
    }

    [Theory]
    // From the MaxMind public test fixture (GeoIP2-Country-Test.mmdb).
    [InlineData("81.2.69.142", "GB")]
    [InlineData("89.160.20.112", "SE")]
    [InlineData("67.43.156.1", "BT")]
    public void ResolveCountry_KnownIp_ReturnsIsoCode(string ip, string expected)
    {
        var result = _resolver.ResolveCountry(new DefaultHttpContext(), ip);
        Assert.Equal(expected, result);
    }
}
