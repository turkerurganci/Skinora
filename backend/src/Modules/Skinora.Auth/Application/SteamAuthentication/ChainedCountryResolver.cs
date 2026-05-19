using Microsoft.AspNetCore.Http;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Tries each inner <see cref="ICountryResolver"/> in order and returns
/// the first non-null code. The production composition (T83 / 08 §8) is:
/// <list type="number">
///   <item><see cref="HeaderCountryResolver"/> — trusts the edge
///   (Cloudflare <c>CF-IPCountry</c>, AWS CloudFront viewer country, nginx
///   GeoIP module). Cheapest, closest to user truth.</item>
///   <item><see cref="MaxMindCountryResolver"/> — embedded MMDB fallback
///   when the edge doesn't set the header (self-hosted nginx, direct
///   testcontainers).</item>
/// </list>
/// Both layers fail open with <c>null</c>; the pipeline then allows the
/// login (geo-block fails open per <see cref="SettingsBasedGeoBlockCheck"/>
/// fail-open semantics so misconfiguration never locks users out).
/// </summary>
public sealed class ChainedCountryResolver : ICountryResolver
{
    private readonly IReadOnlyList<ICountryResolver> _resolvers;

    public ChainedCountryResolver(IEnumerable<ICountryResolver> resolvers)
    {
        _resolvers = resolvers.ToList();
    }

    public string? ResolveCountry(HttpContext? httpContext, string? ipAddress)
    {
        foreach (var resolver in _resolvers)
        {
            var code = resolver.ResolveCountry(httpContext, ipAddress);
            if (!string.IsNullOrWhiteSpace(code)) return code;
        }
        return null;
    }
}
