using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Skinora.API.RateLimiting;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Application.TosAcceptance;
using Skinora.Auth.Configuration;

namespace Skinora.API.Controllers;

/// <summary>
/// Steam OpenID authentication + ToS endpoints — 07 §4.2–§4.4 (A1, A2, A3).
/// Refresh / me / logout arrive with T32, re-verify with T31.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[RateLimit("auth")]
public sealed class AuthController : ControllerBase
{
    private const string ReturnUrlStateCookie = "skinora_oid_rt";
    private const string RefreshCookieName = "refreshToken";
    private const string RefreshCookiePath = "/api/v1/auth";

    private readonly SteamOpenIdSettings _settings;
    private readonly IReturnUrlValidator _returnUrlValidator;
    private readonly ISteamAuthenticationPipeline _pipeline;
    private readonly ITosAcceptanceService _tosAcceptance;

    public AuthController(
        IOptions<SteamOpenIdSettings> settings,
        IReturnUrlValidator returnUrlValidator,
        ISteamAuthenticationPipeline pipeline,
        ITosAcceptanceService tosAcceptance)
    {
        _settings = settings.Value;
        _returnUrlValidator = returnUrlValidator;
        _pipeline = pipeline;
        _tosAcceptance = tosAcceptance;
    }

    /// <summary>A1 — <c>GET /auth/steam</c>. Redirects to Steam OpenID.</summary>
    [HttpGet("steam")]
    [AllowAnonymous]
    public IActionResult InitiateLogin([FromQuery(Name = "returnUrl")] string? returnUrl)
    {
        var sanitized = _returnUrlValidator.Sanitize(returnUrl);

        Response.Cookies.Append(ReturnUrlStateCookie, sanitized, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/auth",
            MaxAge = TimeSpan.FromMinutes(10),
        });

        return Redirect(SteamOpenIdUrlBuilder.Build(_settings));
    }

    /// <summary>A2 — <c>GET /auth/steam/callback</c>. Validates and issues tokens.</summary>
    [HttpGet("steam/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleCallback(CancellationToken cancellationToken)
    {
        var callbackParameters = Request.Query
            .Where(q => q.Key.StartsWith("openid.", StringComparison.Ordinal))
            .ToDictionary(q => q.Key, q => q.Value.ToString());

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;

        var outcome = await _pipeline.ExecuteAsync(
            callbackParameters, ipAddress, userAgent, cancellationToken);

        var storedReturnUrl = Request.Cookies.TryGetValue(ReturnUrlStateCookie, out var stored)
            ? _returnUrlValidator.Sanitize(stored)
            : _settings.DefaultReturnPath;

        Response.Cookies.Delete(ReturnUrlStateCookie, new CookieOptions
        {
            Path = "/api/v1/auth",
        });

        return outcome switch
        {
            AuthenticationOutcome.Success success => IssueSessionAndRedirect(success, storedReturnUrl),
            AuthenticationOutcome.AuthFailed => Redirect(BuildFrontendUrl("error", "auth_failed", null)),
            AuthenticationOutcome.AccountBanned => Redirect(BuildFrontendUrl("error", "account_banned", null)),
            AuthenticationOutcome.GeoBlocked => Redirect(BuildFrontendUrl("error", "geo_blocked", null)),
            AuthenticationOutcome.SanctionsMatch => Redirect(BuildFrontendUrl("error", "sanctions_match", null)),
            AuthenticationOutcome.AgeBlocked => Redirect(BuildFrontendUrl("error", "age_blocked", null)),
            _ => Redirect(BuildFrontendUrl("error", "auth_failed", null)),
        };
    }

    private IActionResult IssueSessionAndRedirect(
        AuthenticationOutcome.Success success, string returnUrl)
    {
        Response.Cookies.Append(RefreshCookieName, success.RefreshToken.PlainTextToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            Expires = success.RefreshToken.ExpiresAt,
        });

        var status = success.IsNewUser ? "new_user" : "success";
        return Redirect(BuildFrontendUrl("status", status, returnUrl));
    }

    /// <summary>A3 — <c>POST /auth/tos/accept</c>. Captures ToS acceptance and 18+ self-attestation.</summary>
    [HttpPost("tos/accept")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    public async Task<ActionResult<AcceptTosResponse>> AcceptTos(
        [FromBody] AcceptTosRequest request, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(AuthClaimTypes.UserId);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var result = await _tosAcceptance.AcceptAsync(
            userId,
            request.TosVersion,
            request.AgeOver18,
            cancellationToken);

        return Ok(new AcceptTosResponse(Accepted: true, AcceptedAt: result.AcceptedAt));
    }

    private string BuildFrontendUrl(string key, string value, string? returnUrl)
    {
        var builder = new UriBuilder(_settings.FrontendCallbackUrl);
        var query = string.IsNullOrEmpty(builder.Query) ? "" : builder.Query.TrimStart('?');
        var qs = string.IsNullOrEmpty(query) ? "" : query + "&";
        qs += $"{key}={Uri.EscapeDataString(value)}";
        if (!string.IsNullOrWhiteSpace(returnUrl))
            qs += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        builder.Query = qs;
        return builder.Uri.ToString();
    }
}

public sealed record AcceptTosRequest(string TosVersion, bool AgeOver18);

public sealed record AcceptTosResponse(bool Accepted, DateTime AcceptedAt);
