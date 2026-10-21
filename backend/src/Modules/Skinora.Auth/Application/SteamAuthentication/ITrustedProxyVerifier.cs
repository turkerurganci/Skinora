using Microsoft.AspNetCore.Http;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// T166 — answers "did this request reach the backend through our own reverse
/// proxy?" by looking at the <b>direct TCP peer</b>, never at a header the
/// client could have written.
/// </summary>
/// <remarks>
/// <see cref="HeaderCountryResolver"/> uses it before believing
/// <c>X-Country-Code</c>. The implementation (<c>ReverseProxyTrustVerifier</c>
/// in Skinora.API) compares the socket peer captured BEFORE
/// <c>UseForwardedHeaders</c> rewrites <c>RemoteIpAddress</c> against the same
/// <c>ReverseProxy:KnownNetworks/KnownProxies</c> list the forwarded-headers
/// trust uses — one trust decision, not two.
/// </remarks>
public interface ITrustedProxyVerifier
{
    bool IsFromTrustedProxy(HttpContext httpContext);
}
