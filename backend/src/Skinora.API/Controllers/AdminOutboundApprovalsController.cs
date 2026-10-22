using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.API.Services.OutboundApprovals;
using Skinora.Auth.Configuration;
using Skinora.Shared.Models;

namespace Skinora.API.Controllers;

/// <summary>
/// AD40 / AD41 — admin approval of outbound payouts and refunds while the
/// launch gate <c>outbound.auto_send_enabled</c> is closed (T169 — 07 §9.33).
/// ⚠ PARA. Every action needs <c>MANAGE_SETTINGS</c>, the permission the other
/// wallet operations (AD20, AD33–AD39) use; the approver is the authenticated
/// admin, never anything in the request.
/// </summary>
[ApiController]
[Route("api/v1/admin/wallets/outbound-approvals")]
public sealed class AdminOutboundApprovalsController : ControllerBase
{
    private const string PolicyManageSettings =
        AuthPolicies.PermissionPrefix + "MANAGE_SETTINGS";

    private readonly IOutboundApprovalService _service;

    public AdminOutboundApprovalsController(IOutboundApprovalService service)
    {
        _service = service;
    }

    /// <summary>AD40 — <c>GET /admin/wallets/outbound-approvals</c>.</summary>
    [HttpGet("")]
    [Authorize(Policy = PolicyManageSettings)]
    [RateLimit("admin-read")]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await _service.ListPendingAsync(cancellationToken));

    /// <summary>AD41 — <c>POST /admin/wallets/outbound-approvals/:id/approve</c>.</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = PolicyManageSettings)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        if (!Guid.TryParse(claim, out var adminId)) return Unauthorized();

        var result = await _service.ApproveAsync(
            adminId, id, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        if (result.Error is null) return Ok(result.Value);

        var (status, code) = result.Error.Value switch
        {
            ApprovalError.RowNotFound => (StatusCodes.Status404NotFound, "ROW_NOT_FOUND"),
            ApprovalError.RowNotApprovable => (StatusCodes.Status422UnprocessableEntity, "ROW_NOT_APPROVABLE"),
            ApprovalError.RowNotOpen => (StatusCodes.Status409Conflict, "ROW_NOT_OPEN"),
            ApprovalError.AlreadyApproved => (StatusCodes.Status409Conflict, "ALREADY_APPROVED"),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR"),
        };
        return StatusCode(status, ApiResponse<object>.Fail(
            code, result.Message ?? code, traceId: HttpContext.TraceIdentifier));
    }
}
