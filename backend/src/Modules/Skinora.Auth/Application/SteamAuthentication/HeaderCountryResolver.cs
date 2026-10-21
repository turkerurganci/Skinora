using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Reads the ISO country code from the <c>X-Country-Code</c> header — but only
/// when the request came through our own reverse proxy (T166).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the trust check (T166):</b> the header used to be read from any
/// request. A client in a banned country could send
/// <c>X-Country-Code: US</c> and the header — consulted before the MaxMind
/// fallback — won, so the geo-block (02 §21.1) was one request header away
/// from being bypassed.
/// </para>
/// <para>
/// <b>Trust chain (deneme yayını: Cloudflare Tunnel → nginx → backend,
/// sahip kararı 2026-10-21):</b>
/// <list type="number">
///   <item>Cloudflare sets <c>CF-IPCountry</c> on every request it forwards
///   and overwrites any value the client sent.</item>
///   <item>nginx (<c>nginx/nginx.conf</c>, <c>$skinora_trusted_edge</c>)
///   copies <c>CF-IPCountry</c> into <c>X-Country-Code</c> ONLY when its own
///   socket peer is the tunnel connector; for every other peer it sends the
///   header empty, which drops whatever the client supplied.</item>
///   <item>The backend (here) believes <c>X-Country-Code</c> only when its
///   socket peer is nginx (<see cref="ITrustedProxyVerifier"/>,
///   <c>ReverseProxy:KnownNetworks</c>). A request that reaches the backend
///   directly carries no trusted country, whatever its headers say.</item>
/// </list>
/// An untrusted or missing header returns <c>null</c>; the chain then falls
/// back to <see cref="MaxMindCountryResolver"/> on the (forwarded-header
/// trusted) client IP, and if that fails too the geo-block fails open — the
/// documented posture: misconfiguration costs information, not lock-outs.
/// </para>
/// <para>
/// Cloudflare's <c>XX</c> ("unknown") is treated as no answer so the MaxMind
/// fallback still gets a chance; <c>T1</c> (Tor) is passed through as-is, so
/// an admin may list it in <c>auth.banned_countries</c>.
/// </para>
/// </remarks>
public sealed class HeaderCountryResolver : ICountryResolver
{
    public const string HeaderName = "X-Country-Code";
    private const string CloudflareUnknown = "XX";

    private readonly ITrustedProxyVerifier _trustedProxy;
    private readonly ILogger<HeaderCountryResolver> _logger;

    public HeaderCountryResolver(
        ITrustedProxyVerifier trustedProxy,
        ILogger<HeaderCountryResolver>? logger = null)
    {
        _trustedProxy = trustedProxy;
        _logger = logger ?? NullLogger<HeaderCountryResolver>.Instance;
    }

    public string? ResolveCountry(HttpContext? httpContext, string? ipAddress)
    {
        if (httpContext is null) return null;

        if (!httpContext.Request.Headers.TryGetValue(HeaderName, out var values))
            return null;

        var raw = values.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (!_trustedProxy.IsFromTrustedProxy(httpContext))
        {
            // Debug, not Warning: anyone can send the header, and a log line
            // per forged request would hand an attacker a log-flooding lever.
            _logger.LogDebug(
                "Ignoring {Header} from an untrusted peer — country left to the IP-based fallback.",
                HeaderName);
            return null;
        }

        // Header may carry 'XX' or 'XX,YY' (proxied chains). Take the first
        // value and normalize to upper-case.
        var first = raw.Split(',', 2)[0].Trim();
        if (first.Length != 2) return null;

        var code = first.ToUpperInvariant();
        return code == CloudflareUnknown ? null : code;
    }
}
