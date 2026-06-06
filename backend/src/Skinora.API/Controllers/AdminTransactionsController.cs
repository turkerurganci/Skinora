using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Shared.Enums;
using Skinora.Shared.Models;
using Skinora.Transactions.Application.Admin;

namespace Skinora.API.Controllers;

/// <summary>
/// Admin-facing transaction endpoints — read surfaces from T63 (AD6 / AD7,
/// 07 §9.6 / §9.7) plus the lifecycle actions from T59 (AD19 / AD19b /
/// AD19c, 07 §9.20–§9.22, 02 §7, 03 §8.8). Each action is protected by
/// the dynamic <c>Permission:&lt;KEY&gt;</c> policy (T06 / T40):
/// reads require <c>VIEW_TRANSACTIONS</c>; AD19 requires
/// <c>CANCEL_TRANSACTIONS</c>; AD19b/c require <c>EMERGENCY_HOLD</c>.
/// AD19/AD19b/AD19c are independent (02 §7 not).
/// </summary>
[ApiController]
[Route("api/v1/admin/transactions")]
public sealed class AdminTransactionsController : ControllerBase
{
    private const string PolicyViewTransactions =
        AuthPolicies.PermissionPrefix + "VIEW_TRANSACTIONS";

    private const string PolicyCancelTransactions =
        AuthPolicies.PermissionPrefix + "CANCEL_TRANSACTIONS";

    private const string PolicyEmergencyHold =
        AuthPolicies.PermissionPrefix + "EMERGENCY_HOLD";

    private readonly IAdminTransactionService _service;
    private readonly IAdminTransactionQueryService _queries;

    public AdminTransactionsController(
        IAdminTransactionService service,
        IAdminTransactionQueryService queries)
    {
        _service = service;
        _queries = queries;
    }

    /// <summary>AD6 — <c>GET /admin/transactions</c> (07 §9.6).</summary>
    [HttpGet("")]
    [Authorize(Policy = PolicyViewTransactions)]
    [RateLimit("admin-read")]
    public async Task<ActionResult<PagedResult<AdminTransactionListItemDto>>> List(
        [FromQuery] TransactionStatus? status,
        [FromQuery] AdminTransactionStatusGroup? statusGroup,
        [FromQuery] StablecoinType? stablecoin,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] decimal? minAmount,
        [FromQuery] decimal? maxAmount,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new AdminTransactionListQuery(
            Status: status,
            StatusGroup: statusGroup,
            Stablecoin: stablecoin,
            DateFrom: dateFrom,
            DateTo: dateTo,
            MinAmount: minAmount,
            MaxAmount: maxAmount,
            Search: search,
            SortBy: sortBy,
            SortOrder: sortOrder,
            Page: page,
            PageSize: pageSize);

        var result = await _queries.ListAsync(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>AD7 — <c>GET /admin/transactions/:id</c> (07 §9.7).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyViewTransactions)]
    [RateLimit("admin-read")]
    public async Task<IActionResult> GetDetail(
        Guid id, CancellationToken cancellationToken)
    {
        var detail = await _queries.GetDetailAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound(ApiResponse<object>.Fail(
                AdminTransactionErrorCodes.TransactionNotFound,
                $"Transaction '{id}' was not found.",
                traceId: HttpContext.TraceIdentifier));
        }
        return Ok(detail);
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

    /// <summary>
    /// AD19d — <c>POST /admin/transactions/hold-by-user/:userId</c>. Bulk
    /// emergency hold over every active transaction of the given user — backs
    /// the 04 §8.3 account-flag "Hold" action (03 §8.8). Same
    /// <c>EMERGENCY_HOLD</c> permission as AD19b/c.
    /// </summary>
    [HttpPost("hold-by-user/{userId:guid}")]
    [Authorize(Policy = PolicyEmergencyHold)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> HoldByUser(
        Guid userId,
        [FromBody] HoldUserTransactionsRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        if (request is null)
            return BadRequest(ApiResponse<object>.Fail(
                AdminTransactionErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _service.HoldAllUserTransactionsAsync(
            adminId, userId, request, ipAddress, cancellationToken);

        return outcome.Status switch
        {
            HoldUserTransactionsStatus.Applied => Ok(outcome.Body),

            HoldUserTransactionsStatus.ValidationFailed => BadRequest(ApiResponse<object>.Fail(
                outcome.ErrorCode ?? AdminTransactionErrorCodes.ValidationError,
                outcome.ErrorMessage ?? "Bulk emergency hold could not be applied.",
                traceId: HttpContext.TraceIdentifier)),

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
