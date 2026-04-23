using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Skinora.API.RateLimiting;
using Skinora.Auth.Application.MobileAuthenticator;
using Skinora.Auth.Application.ReAuthentication;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Application.TosAcceptance;
using Skinora.Auth.Configuration;

namespace Skinora.API.Controllers;

/// <summary>
/// Steam OpenID authentication + ToS + re-verify + authenticator endpoints —
/// 07 §4.2–§4.4, §4.6–§4.8. Refresh / me / logout arrive with T32.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[RateLimit("auth")]
public sealed class AuthController : ControllerBase
{
    private const string ReturnUrlStateCookie = "skinora_oid_rt";
    private const string ReVerifyStateCookie = "skinora_oid_rv";
    private const string RefreshCookieName = "refreshToken";
    private const string RefreshCookiePath = "/api/v1/auth";

    private readonly SteamOpenIdSettings _settings;
    private readonly IReturnUrlValidator _returnUrlValidator;
    private readonly ISteamAuthenticationPipeline _pipeline;
    private readonly ITosAcceptanceService _tosAcceptance;
    private readonly IReAuthPipeline _reAuthPipeline;
    private readonly IMobileAuthenticatorCheck _authenticatorCheck;

    public AuthController(
        IOptions<SteamOpenIdSettings> settings,
        IReturnUrlValidator returnUrlValidator,
        ISteamAuthenticationPipeline pipeline,
        ITosAcceptanceService tosAcceptance,
        IReAuthPipeline reAuthPipeline,
        IMobileAuthenticatorCheck authenticatorCheck)
    {
        _settings = settings.Value;
        _returnUrlValidator = returnUrlValidator;
        _pipeline = pipeline;
        _tosAcceptance = tosAcceptance;
        _reAuthPipeline = reAuthPipeline;
        _authenticatorCheck = authenticatorCheck;
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

    /// <summary>A5 — <c>POST /auth/steam/re-verify</c>. Starts Steam re-auth (wallet change).</summary>
    [HttpPost("steam/re-verify")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    public ActionResult<ReVerifyInitiateResponse> InitiateReVerify(
        [FromBody] ReVerifyInitiateRequest request)
    {
        var userIdClaim = User.FindFirstValue(AuthClaimTypes.UserId);
        var steamIdClaim = User.FindFirstValue(AuthClaimTypes.SteamId);
        if (!Guid.TryParse(userIdClaim, out var userId) || string.IsNullOrWhiteSpace(steamIdClaim))
            return Unauthorized();

        var initiation = _reAuthPipeline.Initiate(userId, steamIdClaim, request.ReturnUrl);

        Response.Cookies.Append(ReVerifyStateCookie, initiation.ProtectedState, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/auth",
            MaxAge = ReAuthStateProtector.StateLifetime,
        });

        return Ok(new ReVerifyInitiateResponse(initiation.SteamAuthUrl));
    }

    /// <summary>A6 — <c>GET /auth/steam/re-verify/callback</c>. Issues reAuthToken.</summary>
    [HttpGet("steam/re-verify/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleReVerifyCallback(CancellationToken cancellationToken)
    {
        // 07 §4.7 — Referrer-Policy: same-origin keeps the reAuthToken query
        // param from leaking to third-party origins via HTTP Referer.
        Response.Headers["Referrer-Policy"] = "same-origin";

        var callbackParameters = Request.Query
            .Where(q => q.Key.StartsWith("openid.", StringComparison.Ordinal))
            .ToDictionary(q => q.Key, q => q.Value.ToString());

        var protectedState = Request.Cookies.TryGetValue(ReVerifyStateCookie, out var state)
            ? state
            : null;

        Response.Cookies.Delete(ReVerifyStateCookie, new CookieOptions
        {
            Path = "/api/v1/auth",
        });

        var outcome = await _reAuthPipeline.HandleCallbackAsync(
            callbackParameters, protectedState, cancellationToken);

        return outcome switch
        {
            ReAuthOutcome.Success success => Redirect(BuildReVerifyRedirect(success)),
            ReAuthOutcome.SteamIdMismatch => Redirect(
                BuildFrontendUrl("error", "steam_id_mismatch", null)),
            ReAuthOutcome.StateMissing => Redirect(
                BuildFrontendUrl("error", "re_verify_failed", null)),
            _ => Redirect(BuildFrontendUrl("error", "re_verify_failed", null)),
        };
    }

    /// <summary>A7 — <c>POST /auth/check-authenticator</c>. Mobile authenticator check.</summary>
    [HttpPost("check-authenticator")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    public async Task<ActionResult<CheckAuthenticatorResponse>> CheckAuthenticator(
        [FromBody] CheckAuthenticatorRequest request, CancellationToken cancellationToken)
    {
        var steamIdClaim = User.FindFirstValue(AuthClaimTypes.SteamId);
        if (string.IsNullOrWhiteSpace(steamIdClaim))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.TradeOfferAccessToken))
            return BadRequest(new { error = "tradeOfferAccessToken is required." });

        var result = await _authenticatorCheck.CheckAsync(
            steamIdClaim, request.TradeOfferAccessToken, cancellationToken);

        return Ok(new CheckAuthenticatorResponse(result.Active, result.SetupGuideUrl));
    }

    private string BuildReVerifyRedirect(ReAuthOutcome.Success success)
    {
        var target = _returnUrlValidator.Sanitize(success.ReturnUrl);
        var separator = target.Contains('?') ? "&" : "?";
        return $"{target}{separator}reAuthToken={Uri.EscapeDataString(success.ReAuthToken)}";
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

public sealed record ReVerifyInitiateRequest(string Purpose, string? ReturnUrl);

public sealed record ReVerifyInitiateResponse(string SteamAuthUrl);

public sealed record CheckAuthenticatorRequest(string TradeOfferAccessToken);

public sealed record CheckAuthenticatorResponse(bool Active, string? SetupGuideUrl);
