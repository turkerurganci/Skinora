using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.API.Services;

namespace Skinora.API.Controllers;

/// <summary>
/// Public platform endpoints — 07 §10 (T63a). Anonymous, rate-limited
/// under the <c>public</c> bucket.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/platform")]
public sealed class PlatformController : ControllerBase
{
    private readonly IPlatformPublicService _service;

    public PlatformController(IPlatformPublicService service)
    {
        _service = service;
    }

    /// <summary>P1 — <c>GET /platform/stats</c> (07 §10.1).</summary>
    [HttpGet("stats")]
    [RateLimit("public")]
    public async Task<ActionResult<PlatformStatsResponse>> GetStats(
        CancellationToken cancellationToken)
    {
        var result = await _service.GetStatsAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>P2 — <c>GET /platform/maintenance</c> (07 §10.2).</summary>
    [HttpGet("maintenance")]
    [RateLimit("public")]
    public async Task<ActionResult<PlatformMaintenanceResponse>> GetMaintenance(
        CancellationToken cancellationToken)
    {
        var result = await _service.GetMaintenanceAsync(cancellationToken);
        return Ok(result);
    }
}
