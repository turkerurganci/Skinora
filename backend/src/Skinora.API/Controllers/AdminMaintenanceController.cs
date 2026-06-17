using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.API.Services;
using Skinora.Auth.Configuration;
using Skinora.Platform.Application.Settings;
using Skinora.Shared.Models;

namespace Skinora.API.Controllers;

/// <summary>
/// Admin maintenance/outage control — WP7 (07 §9.31). Enters/leaves platform
/// maintenance or a Steam/blockchain outage window: persists the
/// <c>platform.maintenance.*</c> banner settings, bulk-freezes/resumes the
/// affected transaction timeouts, and broadcasts the <c>MaintenanceStatusChanged</c>
/// realtime push — all atomically. Guarded by <c>MANAGE_SETTINGS</c> (maintenance
/// is a platform-settings operation; reuses the AD9 permission).
/// </summary>
[ApiController]
[Route("api/v1/admin/maintenance")]
public sealed class AdminMaintenanceController : ControllerBase
{
    private const string PolicyManageSettings =
        AuthPolicies.PermissionPrefix + "MANAGE_SETTINGS";

    private readonly IAdminMaintenanceService _service;

    public AdminMaintenanceController(IAdminMaintenanceService service)
    {
        _service = service;
    }

    /// <summary>WP7 — <c>POST /admin/maintenance/freeze</c>.</summary>
    [HttpPost("freeze")]
    [Authorize(Policy = PolicyManageSettings)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> Freeze(
        [FromBody] MaintenanceFreezeRequest? request,
        CancellationToken cancellationToken)
    {
        if (GetCallerUserId() is not { } adminId)
            return Unauthorized();

        if (request is null)
        {
            return BadRequest(ApiResponse<object>.Fail(
                SettingsErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _service.FreezeAsync(adminId, request, ipAddress, cancellationToken);
        return Map(outcome);
    }

    /// <summary>WP7 — <c>POST /admin/maintenance/resume</c>.</summary>
    [HttpPost("resume")]
    [Authorize(Policy = PolicyManageSettings)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> Resume(CancellationToken cancellationToken)
    {
        if (GetCallerUserId() is not { } adminId)
            return Unauthorized();

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _service.ResumeAsync(adminId, ipAddress, cancellationToken);
        return Map(outcome);
    }

    private IActionResult Map(MaintenanceOperationOutcome outcome) => outcome.Status switch
    {
        MaintenanceOperationStatus.Ok => Ok(outcome.State),

        MaintenanceOperationStatus.ValidationFailed => BadRequest(ApiResponse<object>.Fail(
            SettingsErrorCodes.ValidationError,
            outcome.ErrorMessage ?? "Invalid maintenance request.",
            traceId: HttpContext.TraceIdentifier)),

        _ => StatusCode(StatusCodes.Status500InternalServerError),
    };

    private Guid? GetCallerUserId()
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
