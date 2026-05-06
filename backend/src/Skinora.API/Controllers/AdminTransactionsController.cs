using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Shared.Models;
using Skinora.Transactions.Application.Admin;

namespace Skinora.API.Controllers;

/// <summary>
/// Admin-facing transaction lifecycle endpoints — T59 (07 §9.20–§9.22,
/// 02 §7, 03 §8.8). All three actions are protected by the dynamic
/// <c>Permission:&lt;KEY&gt;</c> policy (T06 / T40):
/// AD19 requires <c>CANCEL_TRANSACTIONS</c>; AD19b/c require
/// <c>EMERGENCY_HOLD</c>. Both are independent (02 §7 not).
/// </summary>
[ApiController]
[Route("api/v1/admin/transactions")]
public sealed class AdminTransactionsController : ControllerBase
{
    private const string PolicyCancelTransactions =
        AuthPolicies.PermissionPrefix + "CANCEL_TRANSACTIONS";

    private const string PolicyEmergencyHold =
        AuthPolicies.PermissionPrefix + "EMERGENCY_HOLD";

    private readonly IAdminTransactionService _service;

    public AdminTransactionsController(IAdminTransactionService service)
    {
        _service = service;
    }

    /// <summary>AD19 — <c>POST /admin/transactions/:id/cancel</c> (07 §9.20).</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PolicyCancelTransactions)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] AdminCancelTransactionRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        if (request is null)
            return BadRequest(ApiResponse<object>.Fail(
                AdminTransactionErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _service.CancelAsync(adminId, id, request, ipAddress, cancellationToken);

        return outcome.Status switch
        {
            AdminCancelTransactionStatus.Cancelled => Ok(outcome.Body),

            AdminCancelTransactionStatus.NotFound => NotFound(CancelEnvelope(outcome)),

            AdminCancelTransactionStatus.ValidationFailed => BadRequest(CancelEnvelope(outcome)),

            AdminCancelTransactionStatus.CannotCancelAtDeliveryStage => UnprocessableEntity(
                CancelEnvelope(outcome)),

            AdminCancelTransactionStatus.InvalidStateTransition => Conflict(CancelEnvelope(outcome)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>AD19b — <c>POST /admin/transactions/:id/emergency-hold</c> (07 §9.21).</summary>
    [HttpPost("{id:guid}/emergency-hold")]
    [Authorize(Policy = PolicyEmergencyHold)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> EmergencyHold(
        Guid id,
        [FromBody] ApplyEmergencyHoldRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        if (request is null)
            return BadRequest(ApiResponse<object>.Fail(
                AdminTransactionErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _service.ApplyEmergencyHoldAsync(adminId, id, request, ipAddress, cancellationToken);

        return outcome.Status switch
        {
            ApplyEmergencyHoldStatus.Applied => Ok(outcome.Body),

            ApplyEmergencyHoldStatus.NotFound => NotFound(HoldEnvelope(outcome)),

            ApplyEmergencyHoldStatus.ValidationFailed => BadRequest(HoldEnvelope(outcome)),

            ApplyEmergencyHoldStatus.AlreadyOnHold
                or ApplyEmergencyHoldStatus.InvalidStateTransition
                => Conflict(HoldEnvelope(outcome)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>AD19c — <c>POST /admin/transactions/:id/release-hold</c> (07 §9.22).</summary>
    [HttpPost("{id:guid}/release-hold")]
    [Authorize(Policy = PolicyEmergencyHold)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> ReleaseHold(
        Guid id,
        [FromBody] ReleaseEmergencyHoldRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        if (request is null)
            return BadRequest(ApiResponse<object>.Fail(
                AdminTransactionErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _service.ReleaseEmergencyHoldAsync(adminId, id, request, ipAddress, cancellationToken);

        return outcome.Status switch
        {
            ReleaseEmergencyHoldStatus.Released => Ok(outcome.Body),

            ReleaseEmergencyHoldStatus.NotFound => NotFound(ReleaseEnvelope(outcome)),

            ReleaseEmergencyHoldStatus.ValidationFailed => BadRequest(ReleaseEnvelope(outcome)),

            ReleaseEmergencyHoldStatus.NotOnHold => Conflict(ReleaseEnvelope(outcome)),

            ReleaseEmergencyHoldStatus.CannotCancelDeliveredHold => UnprocessableEntity(
                ReleaseEnvelope(outcome)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private ApiResponse<object> CancelEnvelope(AdminCancelTransactionOutcome outcome) =>
        ApiResponse<object>.Fail(
            outcome.ErrorCode ?? AdminTransactionErrorCodes.ValidationError,
            outcome.ErrorMessage ?? "Admin cancel could not be completed.",
            traceId: HttpContext.TraceIdentifier);

    private ApiResponse<object> HoldEnvelope(ApplyEmergencyHoldOutcome outcome) =>
        ApiResponse<object>.Fail(
            outcome.ErrorCode ?? AdminTransactionErrorCodes.ValidationError,
            outcome.ErrorMessage ?? "Emergency hold could not be applied.",
            traceId: HttpContext.TraceIdentifier);

    private ApiResponse<object> ReleaseEnvelope(ReleaseEmergencyHoldOutcome outcome) =>
        ApiResponse<object>.Fail(
            outcome.ErrorCode ?? AdminTransactionErrorCodes.ValidationError,
            outcome.ErrorMessage ?? "Emergency hold could not be released.",
            traceId: HttpContext.TraceIdentifier);

    private bool TryGetAdminId(out Guid adminId)
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out adminId);
    }
}
