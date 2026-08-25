using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.Admin.Application.Roles;
using Skinora.Admin.Application.Users;
using Skinora.API.RateLimiting;
using Skinora.API.Services;
using Skinora.API.Services.UserSuspension;
using Skinora.Auth.Configuration;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Application.Settings;
using Skinora.Shared.Enums;
using Skinora.Shared.Models;
using Skinora.Transactions.Application.Admin;

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
    // AD15 only — the user directory is the entry point BOTH surfaces need:
    // role assignment (MANAGE_ROLES) and user detail (VIEW_USERS). Holding
    // either is enough to read it; every mutating role endpoint keeps
    // MANAGE_ROLES alone. Closes backlog AdminUsersDirectoryPermissionMismatch,
    // opened when F5 (#274) turned this list into the S20 detail page's only
    // navigable entry point — a VIEW_USERS admin could open a user's detail
    // page but had no way to reach the directory that links to it.
    private const string PolicyViewUsersOrManageRoles =
        AuthPolicies.PermissionPrefix + "VIEW_USERS,MANAGE_ROLES";
    private const string PolicyManageSettings =
        AuthPolicies.PermissionPrefix + "MANAGE_SETTINGS";
    private const string PolicyViewAuditLog =
        AuthPolicies.PermissionPrefix + "VIEW_AUDIT_LOG";
    // VIEW_STEAM_ACCOUNTS / MANAGE_STEAM_RECOVERY policies removed in v3.0 —
    // the platform runs no Steam bot accounts (02 §15, 05 §3.2).
    private const string PolicyManageFlags =
        AuthPolicies.PermissionPrefix + "MANAGE_FLAGS";

    private readonly IAdminRoleService _roles;
    private readonly IAdminUserService _users;
    private readonly ISystemSettingsService _settings;
    private readonly IAuditLogQueryService _auditLogs;
    private readonly IAdminTransactionQueryService _txQueries;
    private readonly IAdminDashboardService _dashboard;
    private readonly IAdminUserSuspensionService _suspension;
    private readonly IAdminMaintenanceService _maintenance;

    public AdminController(
        IAdminRoleService roles,
        IAdminUserService users,
        ISystemSettingsService settings,
        IAuditLogQueryService auditLogs,
        IAdminTransactionQueryService txQueries,
        IAdminDashboardService dashboard,
        IAdminUserSuspensionService suspension,
        IAdminMaintenanceService maintenance)
    {
        _roles = roles;
        _users = users;
        _settings = settings;
        _auditLogs = auditLogs;
        _txQueries = txQueries;
        _dashboard = dashboard;
        _suspension = suspension;
        _maintenance = maintenance;
    }

    // ---------- Dashboard (07 §9.1) ----------

    /// <summary>AD1 — <c>GET /admin/dashboard</c> (07 §9.1, T63).</summary>
    [HttpGet("dashboard")]
    [Authorize(Policy = AuthPolicies.AdminAccess)]
    [RateLimit("admin-read")]
    public async Task<ActionResult<AdminDashboardResponse>> GetDashboard(
        CancellationToken cancellationToken)
    {
        var result = await _dashboard.GetAsync(cancellationToken);
        return Ok(result);
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

    /// <summary>
    /// AD15 — <c>GET /admin/users</c>. Read-only directory; reachable with
    /// <c>VIEW_USERS</c> or <c>MANAGE_ROLES</c> (07 §9.15).
    /// </summary>
    [HttpGet("users")]
    [Authorize(Policy = PolicyViewUsersOrManageRoles)]
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

    /// <summary>
    /// AD16b — <c>GET /admin/users/:steamId/transactions</c>. T39 shipped a
    /// 0-row placeholder; T63 wires it to the real
    /// <see cref="IAdminTransactionQueryService"/> so the response shape is
    /// 1:1 with AD6 narrowed to a single user.
    /// </summary>
    [HttpGet("users/{steamId}/transactions")]
    [Authorize(Policy = PolicyViewUsers)]
    [RateLimit("admin-read")]
    public async Task<IActionResult> GetUserTransactions(
        string steamId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _txQueries.ListForUserAsync(steamId, page, pageSize, cancellationToken);
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

    // ---------- Account suspension (07 §9.26–§9.27, T105a) ----------

    /// <summary>AD20 — <c>POST /admin/users/:userId/suspend</c> (02 §14.0/§16.2, 03 §8.3).</summary>
    [HttpPost("users/{userId:guid}/suspend")]
    [Authorize(Policy = PolicyManageFlags)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> SuspendUser(
        Guid userId,
        [FromBody] SuspendUserRequest? request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCallerUserId();
        if (adminId is null) return Unauthorized();
        if (request is null)
            return BadRequest(ApiResponse<object>.Fail(
                UserSuspensionErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _suspension.SuspendAsync(
            adminId.Value, userId, request, ipAddress, cancellationToken);

        return outcome.Status switch
        {
            SuspendUserStatus.Suspended => Ok(outcome.Body),

            SuspendUserStatus.NotFound => NotFound(ApiResponse<object>.Fail(
                outcome.ErrorCode ?? UserSuspensionErrorCodes.UserNotFound,
                outcome.ErrorMessage ?? "User not found.",
                traceId: HttpContext.TraceIdentifier)),

            SuspendUserStatus.ValidationFailed => BadRequest(ApiResponse<object>.Fail(
                outcome.ErrorCode ?? UserSuspensionErrorCodes.ValidationError,
                outcome.ErrorMessage ?? "Validation failed.",
                traceId: HttpContext.TraceIdentifier)),

            SuspendUserStatus.AlreadySuspended => Conflict(ApiResponse<object>.Fail(
                outcome.ErrorCode ?? UserSuspensionErrorCodes.AlreadySuspended,
                outcome.ErrorMessage ?? "User is already suspended.",
                traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>AD21 — <c>DELETE /admin/users/:userId/suspend</c> (un-suspend).</summary>
    [HttpDelete("users/{userId:guid}/suspend")]
    [Authorize(Policy = PolicyManageFlags)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> UnsuspendUser(
        Guid userId, CancellationToken cancellationToken)
    {
        var adminId = GetCallerUserId();
        if (adminId is null) return Unauthorized();

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _suspension.UnsuspendAsync(
            adminId.Value, userId, ActorType.ADMIN,
            automatic: false, ipAddress, cancellationToken);

        return outcome.Status switch
        {
            UnsuspendUserStatus.Unsuspended => Ok(outcome.Body),

            UnsuspendUserStatus.NotFound => NotFound(ApiResponse<object>.Fail(
                outcome.ErrorCode ?? UserSuspensionErrorCodes.UserNotFound,
                outcome.ErrorMessage ?? "User not found.",
                traceId: HttpContext.TraceIdentifier)),

            UnsuspendUserStatus.NotSuspended => Conflict(ApiResponse<object>.Fail(
                outcome.ErrorCode ?? UserSuspensionErrorCodes.NotSuspended,
                outcome.ErrorMessage ?? "User is not suspended.",
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

        // WP7 — a direct edit of a platform.maintenance.* key bypasses the
        // dedicated /admin/maintenance endpoint, so the 30s public cache and the
        // C08 banner would otherwise go stale. Evict + re-broadcast the read
        // model. (The dedicated endpoint also freezes/resumes timeouts; a raw
        // key edit intentionally does not — see WP7_REPORT known limitations.)
        if (outcome is UpdateSettingOutcome.Success &&
            key.StartsWith("platform.maintenance.", StringComparison.Ordinal))
        {
            await _maintenance.RefreshPublicStateAsync(cancellationToken);
        }

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
