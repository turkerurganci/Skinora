using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Disputes.Application.Admin;
using Skinora.Disputes.Application.Disputes;
using Skinora.Shared.Enums;
using Skinora.Shared.Models;

namespace Skinora.API.Controllers;

/// <summary>
/// Admin dispute resolution endpoints — WP5 / T58 (07 §9.x, 02 §10.4, 03 §6.4).
/// Closes the ESCALATED dead-end: list the queue, inspect a dispute, resolve it
/// in favor of the seller (uphold) or the buyer (unwind → REFUNDED + refund).
/// </summary>
/// <remarks>
/// Authorization uses the dynamic <c>Permission:&lt;KEY&gt;</c> policy
/// (<c>PermissionPolicyProvider</c>): <c>VIEW_DISPUTES</c> for AD27/AD28,
/// <c>MANAGE_DISPUTES</c> for AD29 — catalogued in
/// <see cref="Skinora.Admin.Application.Permissions.PermissionCatalog"/>.
/// </remarks>
[ApiController]
[Route("api/v1/admin/disputes")]
public sealed class AdminDisputesController : ControllerBase
{
    private const string PolicyViewDisputes =
        AuthPolicies.PermissionPrefix + "VIEW_DISPUTES";
    private const string PolicyManageDisputes =
        AuthPolicies.PermissionPrefix + "MANAGE_DISPUTES";

    private readonly IAdminDisputeService _service;

    public AdminDisputesController(IAdminDisputeService service)
    {
        _service = service;
    }

    /// <summary>AD27 — <c>GET /admin/disputes</c> (07 §9.x). Defaults to ESCALATED.</summary>
    [HttpGet("")]
    [Authorize(Policy = PolicyViewDisputes)]
    [RateLimit("admin-read")]
    public async Task<ActionResult<PagedResult<AdminDisputeListItemDto>>> ListDisputes(
        [FromQuery] DisputeStatus? status,
        [FromQuery] DisputeType? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new AdminDisputeListQuery(
            Status: status,
            Type: type,
            Page: page,
            PageSize: pageSize);

        var result = await _service.ListAsync(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>AD28 — <c>GET /admin/disputes/:id</c> (07 §9.x).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyViewDisputes)]
    [RateLimit("admin-read")]
    public async Task<IActionResult> GetDispute(
        Guid id, CancellationToken cancellationToken)
    {
        var detail = await _service.GetAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound(ApiResponse<object>.Fail(
                DisputeErrorCodes.DisputeNotFound,
                $"Dispute '{id}' was not found.",
                traceId: HttpContext.TraceIdentifier));
        }
        return Ok(detail);
    }

    /// <summary>AD29 — <c>POST /admin/disputes/:id/resolve</c> (07 §9.x).</summary>
    [HttpPost("{id:guid}/resolve")]
    [Authorize(Policy = PolicyManageDisputes)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> ResolveDispute(
        Guid id,
        [FromBody] AdminResolveDisputeRequest? request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCallerUserId();
        if (adminId is null) return Unauthorized();
        if (request is null)
            return BadRequest(ApiResponse<object>.Fail(
                DisputeErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _service.ResolveAsync(
            adminId.Value, id, request, ipAddress, cancellationToken);

        return outcome.Status switch
        {
            AdminResolveDisputeStatus.Resolved => Ok(outcome.Body),

            AdminResolveDisputeStatus.NotFound => NotFound(ResolveEnvelope(outcome)),

            AdminResolveDisputeStatus.NotEscalated
                or AdminResolveDisputeStatus.TransactionOnHold
                or AdminResolveDisputeStatus.InvalidStateTransition
                => Conflict(ResolveEnvelope(outcome)),

            AdminResolveDisputeStatus.ValidationFailed => BadRequest(ResolveEnvelope(outcome)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private ApiResponse<object> ResolveEnvelope(AdminResolveDisputeOutcome outcome) =>
        ApiResponse<object>.Fail(
            outcome.ErrorCode ?? DisputeErrorCodes.ValidationError,
            outcome.ErrorMessage ?? "Dispute could not be resolved.",
            traceId: HttpContext.TraceIdentifier);

    private Guid? GetCallerUserId()
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
