using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.Admin.Application.Roles;
using Skinora.Admin.Application.Users;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Application.Settings;
using Skinora.Shared.Models;

namespace Skinora.API.Controllers;

/// <summary>
/// Admin role + user management endpoints — 07 §9.11–§9.18 (T39).
/// </summary>
/// <remarks>
/// Authorization uses the dynamic <c>Permission:&lt;KEY&gt;</c> policy
/// (T06 — <c>PermissionPolicyProvider</c>). Until JWT issuance starts
/// emitting <c>permission</c> claims (T40), only super-admins reach these
/// endpoints — <c>PermissionAuthorizationHandler</c> bypasses the
/// requirement when <c>role = super_admin</c>.
/// </remarks>
[ApiController]
[Route("api/v1/admin")]
public sealed class AdminController : ControllerBase
{
    // Compile-time policy names — concatenation of two const strings is a
    // valid attribute argument in C#.
    private const string PolicyManageRoles =
        AuthPolicies.PermissionPrefix + "MANAGE_ROLES";
    private const string PolicyViewUsers =
        AuthPolicies.PermissionPrefix + "VIEW_USERS";
    private const string PolicyManageSettings =
        AuthPolicies.PermissionPrefix + "MANAGE_SETTINGS";
    private const string PolicyViewAuditLog =
        AuthPolicies.PermissionPrefix + "VIEW_AUDIT_LOG";

    private readonly IAdminRoleService _roles;
    private readonly IAdminUserService _users;
    private readonly ISystemSettingsService _settings;
    private readonly IAuditLogQueryService _auditLogs;

    public AdminController(
        IAdminRoleService roles,
        IAdminUserService users,
        ISystemSettingsService settings,
        IAuditLogQueryService auditLogs)
    {
        _roles = roles;
        _users = users;
        _settings = settings;
        _auditLogs = auditLogs;
    }

    // ---------- Roles (07 §9.11–§9.14) ----------

    /// <summary>AD11 — <c>GET /admin/roles</c>.</summary>
    [HttpGet("roles")]
    [Authorize(Policy = PolicyManageRoles)]
    [RateLimit("admin-read")]
    public async Task<ActionResult<RolesListResponse>> ListRoles(
        CancellationToken cancellationToken)
    {
        var result = await _roles.ListAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>AD12 — <c>POST /admin/roles</c>.</summary>
    [HttpPost("roles")]
    [Authorize(Policy = PolicyManageRoles)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> CreateRole(
        [FromBody] CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await _roles.CreateAsync(request, cancellationToken);
        return MapRoleOperationOutcome(outcome, successStatus: StatusCodes.Status201Created);
    }

    /// <summary>AD13 — <c>PUT /admin/roles/:id</c>.</summary>
    [HttpPut("roles/{id:guid}")]
    [Authorize(Policy = PolicyManageRoles)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> UpdateRole(
        Guid id,
        [FromBody] UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await _roles.UpdateAsync(id, request, cancellationToken);
        return MapRoleOperationOutcome(outcome, successStatus: StatusCodes.Status200OK);
    }

    /// <summary>AD14 — <c>DELETE /admin/roles/:id</c>.</summary>
    [HttpDelete("roles/{id:guid}")]
    [Authorize(Policy = PolicyManageRoles)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> DeleteRole(
        Guid id, CancellationToken cancellationToken)
    {
        var outcome = await _roles.DeleteAsync(id, cancellationToken);
        return outcome switch
        {
            RoleDeleteOutcome.Success => Ok((object?)null),

            RoleDeleteOutcome.NotFound => NotFound(ApiResponse<object>.Fail(
                AdminRoleErrorCodes.RoleNotFound,
                $"Role '{id}' was not found.",
                traceId: HttpContext.TraceIdentifier)),

            RoleDeleteOutcome.HasUsers hasUsers => UnprocessableEntity(
                ApiResponse<object>.Fail(
                    AdminRoleErrorCodes.RoleHasUsers,
                    "Role has assigned users and cannot be deleted.",
                    details: new { hasUsers.AssignedUserCount },
                    traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ---------- Users (07 §9.15–§9.18) ----------

    /// <summary>AD15 — <c>GET /admin/users</c>.</summary>
    [HttpGet("users")]
    [Authorize(Policy = PolicyManageRoles)]
    [RateLimit("admin-read")]
    public async Task<ActionResult<PagedResult<AdminUserListItemDto>>> ListUsers(
        [FromQuery] string? search,
        [FromQuery] Guid? roleId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _users.ListAsync(search, roleId, page, pageSize, cancellationToken);
        return Ok(result);
    }

    /// <summary>AD16 — <c>GET /admin/users/:steamId</c>.</summary>
    [HttpGet("users/{steamId}")]
    [Authorize(Policy = PolicyViewUsers)]
    [RateLimit("admin-read")]
    public async Task<IActionResult> GetUserDetail(
        string steamId, CancellationToken cancellationToken)
    {
        var detail = await _users.GetDetailAsync(steamId, cancellationToken);
        if (detail is null)
        {
            return NotFound(ApiResponse<object>.Fail(
                AdminUserErrorCodes.UserNotFound,
                $"User '{steamId}' was not found.",
                traceId: HttpContext.TraceIdentifier));
        }
        return Ok(detail);
    }

    /// <summary>AD16b — <c>GET /admin/users/:steamId/transactions</c>.</summary>
    [HttpGet("users/{steamId}/transactions")]
    [Authorize(Policy = PolicyViewUsers)]
    [RateLimit("admin-read")]
    public async Task<IActionResult> GetUserTransactions(
        string steamId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _users.GetTransactionsAsync(steamId, page, pageSize, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponse<object>.Fail(
                AdminUserErrorCodes.UserNotFound,
                $"User '{steamId}' was not found.",
                traceId: HttpContext.TraceIdentifier));
        }
        return Ok(result);
    }

    /// <summary>AD17 — <c>PUT /admin/users/:id/role</c>.</summary>
    [HttpPut("users/{id:guid}/role")]
    [Authorize(Policy = PolicyManageRoles)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> AssignRole(
        Guid id,
        [FromBody] AssignRoleRequest request,
        CancellationToken cancellationToken)
    {
        var assigningAdminId = GetCallerUserId();
        var outcome = await _users.AssignRoleAsync(id, request, assigningAdminId, cancellationToken);
        return outcome switch
        {
            AssignRoleOutcome.Success success => Ok(success.Response),

            AssignRoleOutcome.UserNotFound => NotFound(ApiResponse<object>.Fail(
                AdminUserErrorCodes.UserNotFound,
                $"User '{id}' was not found.",
                traceId: HttpContext.TraceIdentifier)),

            AssignRoleOutcome.RoleNotFound => NotFound(ApiResponse<object>.Fail(
                AdminUserErrorCodes.RoleNotFound,
                "Requested role was not found.",
                traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ---------- Platform settings (07 §9.8–§9.9) ----------

    /// <summary>AD8 — <c>GET /admin/settings</c>.</summary>
    [HttpGet("settings")]
    [Authorize(Policy = PolicyManageSettings)]
    [RateLimit("admin-read")]
    public async Task<ActionResult<SettingsListResponse>> ListSettings(
        CancellationToken cancellationToken)
    {
        var result = await _settings.ListAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>AD9 — <c>PUT /admin/settings/:key</c>.</summary>
    [HttpPut("settings/{key}")]
    [Authorize(Policy = PolicyManageSettings)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> UpdateSetting(
        string key,
        [FromBody] UpdateSettingRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = GetCallerUserId();
        if (actorId is null)
        {
            // Authorize attribute should have rejected anonymous calls, but
            // defending in depth keeps the audit row's ActorId NOT NULL guarantee.
            return Unauthorized();
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _settings.UpdateAsync(
            key, request, actorId.Value, ipAddress, cancellationToken);

        return outcome switch
        {
            UpdateSettingOutcome.Success success => Ok(success.Response),

            UpdateSettingOutcome.NotFound notFound => NotFound(ApiResponse<object>.Fail(
                SettingsErrorCodes.SettingNotFound,
                $"Setting '{notFound.Key}' was not found.",
                traceId: HttpContext.TraceIdentifier)),

            UpdateSettingOutcome.ValidationFailed validation => BadRequest(ApiResponse<object>.Fail(
                SettingsErrorCodes.ValidationError,
                validation.Message,
                traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ---------- Audit log (07 §9.19) ----------

    /// <summary>AD18 — <c>GET /admin/audit-logs</c>.</summary>
    [HttpGet("audit-logs")]
    [Authorize(Policy = PolicyViewAuditLog)]
    [RateLimit("admin-read")]
    public async Task<ActionResult<PagedResult<AuditLogListItemDto>>> ListAuditLogs(
        [FromQuery] string? category,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? search,
        [FromQuery] Guid? transactionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new AuditLogListQuery(
            Category: category,
            DateFrom: dateFrom,
            DateTo: dateTo,
            Search: search,
            TransactionId: transactionId,
            Page: page,
            PageSize: pageSize);

        var result = await _auditLogs.ListAsync(query, cancellationToken);
        return Ok(result);
    }

    // ---------- helpers ----------

    private IActionResult MapRoleOperationOutcome(
        RoleOperationOutcome outcome, int successStatus)
        => outcome switch
        {
            RoleOperationOutcome.Success success => StatusCode(successStatus, success.Role),

            RoleOperationOutcome.NotFound => NotFound(ApiResponse<object>.Fail(
                AdminRoleErrorCodes.RoleNotFound,
                "Role was not found.",
                traceId: HttpContext.TraceIdentifier)),

            RoleOperationOutcome.NameConflict => Conflict(ApiResponse<object>.Fail(
                AdminRoleErrorCodes.RoleNameExists,
                "A role with the same name already exists.",
                traceId: HttpContext.TraceIdentifier)),

            RoleOperationOutcome.InvalidPermission invalid => BadRequest(ApiResponse<object>.Fail(
                AdminRoleErrorCodes.InvalidPermission,
                $"Permission '{invalid.Key}' is not in the catalog.",
                details: new { invalid.Key },
                traceId: HttpContext.TraceIdentifier)),

            RoleOperationOutcome.ValidationFailed validation => BadRequest(ApiResponse<object>.Fail(
                AdminRoleErrorCodes.ValidationError,
                validation.Message,
                traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };

    private Guid? GetCallerUserId()
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
