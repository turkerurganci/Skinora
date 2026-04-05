using Microsoft.AspNetCore.Mvc;
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
}
