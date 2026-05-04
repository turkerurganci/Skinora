using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Fraud.Application.Flags;
using Skinora.Shared.Enums;
using Skinora.Shared.Models;

namespace Skinora.API.Controllers;

/// <summary>
/// Admin fraud-flag review endpoints — T54 (07 §9.2–§9.5, 03 §8.2).
/// </summary>
/// <remarks>
/// <para>
/// Authorization uses the dynamic <c>Permission:&lt;KEY&gt;</c> policy
/// (T06 — <c>PermissionPolicyProvider</c>). <c>VIEW_FLAGS</c> for AD2/AD3,
/// <c>MANAGE_FLAGS</c> for AD4/AD5 — keys catalogued in
/// <see cref="Skinora.Admin.Application.Permissions.PermissionCatalog"/>.
/// </para>
/// <para>
/// Until JWT issuance starts emitting <c>permission</c> claims (T40),
/// only super-admins reach these endpoints — the
/// <c>PermissionAuthorizationHandler</c> bypass clears
/// <c>role = super_admin</c>.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/admin/flags")]
public sealed class AdminFlagsController : ControllerBase
{
    // Compile-time policy names — concatenation of two const strings is
    // a valid attribute argument in C#.
    private const string PolicyViewFlags =
        AuthPolicies.PermissionPrefix + "VIEW_FLAGS";
    private const string PolicyManageFlags =
        AuthPolicies.PermissionPrefix + "MANAGE_FLAGS";

    private readonly IFraudFlagAdminQueryService _queries;
    private readonly IFraudFlagService _flags;

    public AdminFlagsController(
        IFraudFlagAdminQueryService queries,
        IFraudFlagService flags)
    {
        _queries = queries;
        _flags = flags;
    }

    /// <summary>AD2 — <c>GET /admin/flags</c> (07 §9.2).</summary>
    [HttpGet("")]
    [Authorize(Policy = PolicyViewFlags)]
    [RateLimit("admin-read")]
    public async Task<ActionResult<FraudFlagListResponse>> ListFlags(
        [FromQuery] FraudFlagType? type,
        [FromQuery] ReviewStatus? reviewStatus,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new FraudFlagListQuery(
            Type: type,
            ReviewStatus: reviewStatus,
            DateFrom: dateFrom,
            DateTo: dateTo,
            SortBy: sortBy,
            SortOrder: sortOrder,
            Page: page,
            PageSize: pageSize);

        var result = await _queries.ListAsync(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>AD3 — <c>GET /admin/flags/:id</c> (07 §9.3).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyViewFlags)]
    [RateLimit("admin-read")]
    public async Task<IActionResult> GetFlag(
        Guid id, CancellationToken cancellationToken)
    {
        var detail = await _queries.GetDetailAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound(ApiResponse<object>.Fail(
                FraudFlagErrorCodes.FlagNotFound,
                $"Flag '{id}' was not found.",
                traceId: HttpContext.TraceIdentifier));
        }
        return Ok(detail);
    }

    /// <summary>AD4 — <c>POST /admin/flags/:id/approve</c> (07 §9.4).</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = PolicyManageFlags)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> ApproveFlag(
        Guid id,
        [FromBody] FraudFlagReviewRequest? request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCallerUserId();
        if (adminId is null) return Unauthorized();

        var outcome = await _flags.ApproveAsync(
            id, adminId.Value, request?.Note, cancellationToken);

        return outcome switch
        {
            ApproveFlagOutcome.Success success => Ok(success.Result),

            ApproveFlagOutcome.NotFound => NotFound(ApiResponse<object>.Fail(
                FraudFlagErrorCodes.FlagNotFound,
                $"Flag '{id}' was not found.",
                traceId: HttpContext.TraceIdentifier)),

            ApproveFlagOutcome.AlreadyReviewed => Conflict(ApiResponse<object>.Fail(
                FraudFlagErrorCodes.AlreadyReviewed,
                "Flag has already been reviewed.",
                traceId: HttpContext.TraceIdentifier)),

            ApproveFlagOutcome.TransactionNotFlagged => Conflict(ApiResponse<object>.Fail(
                FraudFlagErrorCodes.TransactionNotFlagged,
                "Linked transaction is no longer in FLAGGED state.",
                traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>AD5 — <c>POST /admin/flags/:id/reject</c> (07 §9.5).</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = PolicyManageFlags)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> RejectFlag(
        Guid id,
        [FromBody] FraudFlagReviewRequest? request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCallerUserId();
        if (adminId is null) return Unauthorized();

        var outcome = await _flags.RejectAsync(
            id, adminId.Value, request?.Note, cancellationToken);

        return outcome switch
        {
            RejectFlagOutcome.Success success => Ok(success.Result),

            RejectFlagOutcome.NotFound => NotFound(ApiResponse<object>.Fail(
                FraudFlagErrorCodes.FlagNotFound,
                $"Flag '{id}' was not found.",
                traceId: HttpContext.TraceIdentifier)),

            RejectFlagOutcome.AlreadyReviewed => Conflict(ApiResponse<object>.Fail(
                FraudFlagErrorCodes.AlreadyReviewed,
                "Flag has already been reviewed.",
                traceId: HttpContext.TraceIdentifier)),

            RejectFlagOutcome.TransactionNotFlagged => Conflict(ApiResponse<object>.Fail(
                FraudFlagErrorCodes.TransactionNotFlagged,
                "Linked transaction is no longer in FLAGGED state.",
                traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private Guid? GetCallerUserId()
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
