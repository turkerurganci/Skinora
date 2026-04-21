using Microsoft.AspNetCore.Http;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Resolves an incoming request to an ISO-3166-1 alpha-2 country code. MVP
/// uses <see cref="HeaderCountryResolver"/> which reads the
/// <c>X-Country-Code</c> header (set by reverse proxy / CDN during
/// deployment). T83 replaces this with a real geolocation provider
/// (MaxMind GeoLite2 or ipinfo.io) and adds VPN/proxy heuristics.
/// </summary>
public interface ICountryResolver
{
    /// <returns>
    /// Upper-case ISO-3166-1 alpha-2 country code, or <c>null</c> if the
    /// country cannot be determined.
    /// </returns>
    string? ResolveCountry(HttpContext? httpContext, string? ipAddress);
}
