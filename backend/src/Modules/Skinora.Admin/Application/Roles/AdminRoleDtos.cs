namespace Skinora.Admin.Application.Roles;

/// <summary>Body of 07 §9.11 — <c>GET /admin/roles</c> response.</summary>
public sealed record RolesListResponse(
    IReadOnlyList<RoleSummaryDto> Roles,
    IReadOnlyList<AvailablePermissionDto> AvailablePermissions);

/// <summary>One row in <see cref="RolesListResponse.Roles"/> (07 §9.11).</summary>
public sealed record RoleSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSuperAdmin,
    IReadOnlyList<string> Permissions,
    int AssignedUserCount,
    DateTime CreatedAt);

/// <summary>One entry in <see cref="RolesListResponse.AvailablePermissions"/> (07 §9.11).</summary>
public sealed record AvailablePermissionDto(string Key, string Label);

/// <summary>Body of 07 §9.12 — <c>POST /admin/roles</c> request.</summary>
public sealed record CreateRoleRequest(
    string Name,
    string? Description,
    IReadOnlyList<string>? Permissions);

/// <summary>Body of 07 §9.13 — <c>PUT /admin/roles/:id</c> request (07 §9.12 ile aynı).</summary>
public sealed record UpdateRoleRequest(
    string Name,
    string? Description,
    IReadOnlyList<string>? Permissions);

/// <summary>Body of 07 §9.12 / §9.13 success response.</summary>
public sealed record RoleDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSuperAdmin,
    IReadOnlyList<string> Permissions,
    DateTime CreatedAt);
