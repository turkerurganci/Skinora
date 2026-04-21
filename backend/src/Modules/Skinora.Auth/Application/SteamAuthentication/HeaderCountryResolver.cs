using Microsoft.AspNetCore.Http;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Reads the ISO country code from the <c>X-Country-Code</c> header set by
/// the edge (Cloudflare <c>CF-IPCountry</c>, nginx GeoIP module, AWS
/// CloudFront, etc.). In deployments where the edge doesn't provide a
/// country code this resolver returns <c>null</c> — pipeline fails open in
/// that case so misconfiguration doesn't lock users out. T83 supersedes
/// this with an inline geolocation lookup.
/// </summary>
public sealed class HeaderCountryResolver : ICountryResolver
{
    public const string HeaderName = "X-Country-Code";

    public string? ResolveCountry(HttpContext? httpContext, string? ipAddress)
    {
        if (httpContext is null) return null;

        if (!httpContext.Request.Headers.TryGetValue(HeaderName, out var values))
            return null;

        var raw = values.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Header may carry 'XX' or 'XX,YY' (proxied chains). Take the first
        // value and normalize to upper-case.
        var first = raw.Split(',', 2)[0].Trim();
        if (first.Length != 2) return null;

        return first.ToUpperInvariant();
    }
}
