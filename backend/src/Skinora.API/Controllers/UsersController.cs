using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Shared.Models;
using Skinora.Users.Application.Profiles;

namespace Skinora.API.Controllers;

/// <summary>
/// User profile endpoints — 07 §5.1 (U1), §5.2 (U2), §5.5 (U5).
/// </summary>
[ApiController]
[Route("api/v1/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserProfileService _profileService;

    public UsersController(IUserProfileService profileService)
    {
        _profileService = profileService;
    }

    /// <summary>U1 — <c>GET /users/me</c>. Own profile for S08 (07 §5.1).</summary>
    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-read")]
    public async Task<ActionResult<UserProfileDto>> GetMe(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(AuthClaimTypes.UserId);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var dto = await _profileService.GetOwnProfileAsync(userId, cancellationToken);
        if (dto is null) return Unauthorized();

        return Ok(dto);
    }

    /// <summary>U2 — <c>GET /users/me/stats</c>. Dashboard quick stats (07 §5.2).</summary>
    [HttpGet("me/stats")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-read")]
    public async Task<ActionResult<UserStatsDto>> GetMyStats(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(AuthClaimTypes.UserId);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var dto = await _profileService.GetOwnStatsAsync(userId, cancellationToken);
        if (dto is null) return Unauthorized();

        return Ok(dto);
    }

    /// <summary>U5 — <c>GET /users/{steamId}</c>. Public profile (07 §5.5).</summary>
    [HttpGet("{steamId}")]
    [AllowAnonymous]
    [RateLimit("public")]
    public async Task<ActionResult<PublicUserProfileDto>> GetPublic(
        string steamId, CancellationToken cancellationToken)
    {
        var dto = await _profileService.GetPublicProfileAsync(steamId, cancellationToken);
        if (dto is null)
        {
            var body = ApiResponse<object>.Fail(
                "USER_NOT_FOUND",
                $"User '{steamId}' was not found.",
                traceId: HttpContext.TraceIdentifier);
            return NotFound(body);
        }

        return Ok(dto);
    }
}
