using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Shared.Exceptions;

namespace Skinora.API.Controllers;

/// <summary>
/// Internal diagnostics endpoints for development/testing.
/// Will be restricted or removed in production (T06+).
/// </summary>
[ApiController]
[Route("api/v1/_diag")]
public class DiagnosticsController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { message = "pong" });
    }

    [HttpGet("throw/not-found")]
    public IActionResult ThrowNotFound()
    {
        throw new NotFoundException("TestEntity", "abc-123");
    }

    [HttpGet("throw/business-rule")]
    public IActionResult ThrowBusinessRule()
    {
        throw new BusinessRuleException("ACTIVE_TRANSACTION_EXISTS", "Cannot delete account with active transactions.");
    }

    [HttpGet("throw/domain")]
    public IActionResult ThrowDomain()
    {
        throw new DomainException("INVALID_STATE_TRANSITION", "Cannot transition from Created to Completed.");
    }

    [HttpGet("throw/integration")]
    public IActionResult ThrowIntegration()
    {
        throw new IntegrationException("SteamAPI", "Steam API returned 503.");
    }

    [HttpGet("throw/unhandled")]
    public IActionResult ThrowUnhandled()
    {
        throw new InvalidOperationException("Something unexpected happened.");
    }

    // --- Auth test endpoints (T06) ---

    [Authorize]
    [HttpGet("auth/protected")]
    public IActionResult AuthProtected()
    {
        return Ok(new { message = "authenticated", sub = User.FindFirst(AuthClaimTypes.UserId)?.Value });
    }

    [AllowAnonymous]
    [HttpGet("auth/public")]
    public IActionResult AuthPublic()
    {
        return Ok(new { message = "public" });
    }

    [Authorize(Policy = AuthPolicies.AdminAccess)]
    [HttpGet("auth/admin")]
    public IActionResult AuthAdmin()
    {
        return Ok(new { message = "admin" });
    }

    [Authorize(Policy = AuthPolicies.SuperAdmin)]
    [HttpGet("auth/super-admin")]
    public IActionResult AuthSuperAdmin()
    {
        return Ok(new { message = "super_admin" });
    }

    [Authorize(Policy = "Permission:ManageUsers")]
    [HttpGet("auth/permission")]
    public IActionResult AuthPermission()
    {
        return Ok(new { message = "has_permission" });
    }

    // --- Rate limit test endpoints (T07) ---

    [AllowAnonymous]
    [RateLimit("public")]
    [HttpGet("ratelimit/public")]
    public IActionResult RateLimitPublic()
    {
        return Ok(new { message = "public-rate-limited" });
    }

    [AllowAnonymous]
    [RateLimit("auth")]
    [HttpGet("ratelimit/auth")]
    public IActionResult RateLimitAuth()
    {
        return Ok(new { message = "auth-rate-limited" });
    }

    [AllowAnonymous]
    [RateLimit("user-read")]
    [HttpGet("ratelimit/user-read")]
    public IActionResult RateLimitUserRead()
    {
        return Ok(new { message = "user-read-rate-limited" });
    }

    [AllowAnonymous]
    [RateLimit("steam-inventory")]
    [HttpGet("ratelimit/steam-inventory")]
    public IActionResult RateLimitSteamInventory()
    {
        return Ok(new { message = "steam-inventory-rate-limited" });
    }
}
