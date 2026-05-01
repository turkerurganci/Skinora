namespace Skinora.Admin.Application.Roles;

/// <summary>
/// Role + permission management backing 07 §9.11–§9.14. All writes are
/// audit-tracked through <see cref="Skinora.Shared.Domain.IAuditableEntity"/>
/// — the centralised <c>AuditLog</c> entry will be added when T42 wires the
/// service-level audit pipeline; until then the soft-delete + UpdatedAt
/// trail captured by <c>AppDbContext.UpdateAuditFields</c> is sufficient.
/// </summary>
public interface IAdminRoleService
{
    /// <summary>AD11 — list roles + the static <see cref="Permissions.PermissionCatalog"/>.</summary>
    Task<RolesListResponse> ListAsync(CancellationToken cancellationToken);

    /// <summary>AD12 — create a new role.</summary>
    Task<RoleOperationOutcome> CreateAsync(
        CreateRoleRequest request, CancellationToken cancellationToken);

    /// <summary>AD13 — replace a role's metadata + permission set.</summary>
    Task<RoleOperationOutcome> UpdateAsync(
        Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken);

    /// <summary>AD14 — soft-delete a role; refused while users are assigned.</summary>
    Task<RoleDeleteOutcome> DeleteAsync(Guid roleId, CancellationToken cancellationToken);
}
