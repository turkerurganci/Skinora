using Microsoft.EntityFrameworkCore;
using Skinora.Admin.Application.Permissions;
using Skinora.Admin.Domain.Entities;
using Skinora.Shared.Persistence;

namespace Skinora.Admin.Application.Roles;

/// <summary>
/// EF Core-backed implementation of 07 §9.11–§9.14. Soft-delete query
/// filters on <see cref="AdminRole"/>, <see cref="AdminRolePermission"/>
/// and <see cref="AdminUserRole"/> already hide tombstoned rows — every
/// query here works against the active set automatically.
/// </summary>
public sealed class AdminRoleService : IAdminRoleService
{
    private const int NameMaxLength = 100;
    private const int DescriptionMaxLength = 500;

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public AdminRoleService(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<RolesListResponse> ListAsync(CancellationToken cancellationToken)
    {
        var roles = await _db.Set<AdminRole>()
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Description,
                r.IsSuperAdmin,
                r.CreatedAt,
                Permissions = _db.Set<AdminRolePermission>()
                    .Where(p => p.AdminRoleId == r.Id)
                    .OrderBy(p => p.Permission)
                    .Select(p => p.Permission)
                    .ToList(),
                AssignedUserCount = _db.Set<AdminUserRole>()
                    .Count(ur => ur.AdminRoleId == r.Id),
            })
            .ToListAsync(cancellationToken);

        var summaries = roles
            .Select(r => new RoleSummaryDto(
                r.Id,
                r.Name,
                r.Description,
                r.IsSuperAdmin,
                r.Permissions,
                r.AssignedUserCount,
                r.CreatedAt))
            .ToList();

        var available = PermissionCatalog.All
            .Select(p => new AvailablePermissionDto(p.Key, p.Label))
            .ToList();

        return new RolesListResponse(summaries, available);
    }

    public async Task<RoleOperationOutcome> CreateAsync(
        CreateRoleRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.Name, request.Description, request.Permissions, out var validationOutcome))
            return validationOutcome!;

        var name = request.Name.Trim();
        var nameTaken = await _db.Set<AdminRole>()
            .AnyAsync(r => r.Name == name, cancellationToken);
        if (nameTaken) return new RoleOperationOutcome.NameConflict();

        var now = _clock.GetUtcNow().UtcDateTime;
        var role = new AdminRole
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim(),
            IsSuperAdmin = false,
        };

        _db.Set<AdminRole>().Add(role);

        foreach (var key in DistinctPermissionKeys(request.Permissions))
        {
            _db.Set<AdminRolePermission>().Add(new AdminRolePermission
            {
                Id = Guid.NewGuid(),
                AdminRoleId = role.Id,
                Permission = key,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var permissions = DistinctPermissionKeys(request.Permissions).OrderBy(k => k).ToList();
        return new RoleOperationOutcome.Success(new RoleDetailDto(
            role.Id, role.Name, role.Description, role.IsSuperAdmin, permissions, role.CreatedAt));
    }

    public async Task<RoleOperationOutcome> UpdateAsync(
        Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.Name, request.Description, request.Permissions, out var validationOutcome))
            return validationOutcome!;

        var role = await _db.Set<AdminRole>()
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null) return new RoleOperationOutcome.NotFound();

        var name = request.Name.Trim();
        if (!string.Equals(role.Name, name, StringComparison.Ordinal))
        {
            var nameTaken = await _db.Set<AdminRole>()
                .AnyAsync(r => r.Id != roleId && r.Name == name, cancellationToken);
            if (nameTaken) return new RoleOperationOutcome.NameConflict();
            role.Name = name;
        }

        role.Description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();

        var desired = DistinctPermissionKeys(request.Permissions).ToHashSet(StringComparer.Ordinal);
        var existing = await _db.Set<AdminRolePermission>()
            .Where(p => p.AdminRoleId == roleId)
            .ToListAsync(cancellationToken);

        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var current in existing)
        {
            if (!desired.Contains(current.Permission))
            {
                current.IsDeleted = true;
                current.DeletedAt = now;
            }
        }

        var existingKeys = existing
            .Where(p => !p.IsDeleted)
            .Select(p => p.Permission)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var key in desired)
        {
            if (existingKeys.Contains(key)) continue;
            _db.Set<AdminRolePermission>().Add(new AdminRolePermission
            {
                Id = Guid.NewGuid(),
                AdminRoleId = roleId,
                Permission = key,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var permissions = desired.OrderBy(k => k, StringComparer.Ordinal).ToList();
        return new RoleOperationOutcome.Success(new RoleDetailDto(
            role.Id, role.Name, role.Description, role.IsSuperAdmin, permissions, role.CreatedAt));
    }

    public async Task<RoleDeleteOutcome> DeleteAsync(
        Guid roleId, CancellationToken cancellationToken)
    {
        var role = await _db.Set<AdminRole>()
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null) return new RoleDeleteOutcome.NotFound();

        var assignedCount = await _db.Set<AdminUserRole>()
            .CountAsync(ur => ur.AdminRoleId == roleId, cancellationToken);
        if (assignedCount > 0) return new RoleDeleteOutcome.HasUsers(assignedCount);

        var now = _clock.GetUtcNow().UtcDateTime;
        role.IsDeleted = true;
        role.DeletedAt = now;

        var permissions = await _db.Set<AdminRolePermission>()
            .Where(p => p.AdminRoleId == roleId)
            .ToListAsync(cancellationToken);
        foreach (var p in permissions)
        {
            p.IsDeleted = true;
            p.DeletedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new RoleDeleteOutcome.Success();
    }

    /// <summary>Shared input validation for AD12 / AD13.</summary>
    private static bool Validate(
        string? name,
        string? description,
        IReadOnlyList<string>? permissions,
        out RoleOperationOutcome? outcome)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            outcome = new RoleOperationOutcome.ValidationFailed("Role name is required.");
            return true;
        }
        if (name.Trim().Length > NameMaxLength)
        {
            outcome = new RoleOperationOutcome.ValidationFailed(
                $"Role name exceeds {NameMaxLength} characters.");
            return true;
        }
        if (description is not null && description.Length > DescriptionMaxLength)
        {
            outcome = new RoleOperationOutcome.ValidationFailed(
                $"Role description exceeds {DescriptionMaxLength} characters.");
            return true;
        }

        if (permissions is not null)
        {
            foreach (var key in permissions)
            {
                if (!PermissionCatalog.IsKnown(key))
                {
                    outcome = new RoleOperationOutcome.InvalidPermission(key);
                    return true;
                }
            }
        }

        outcome = null;
        return false;
    }

    private static IEnumerable<string> DistinctPermissionKeys(IReadOnlyList<string>? permissions)
        => permissions is null
            ? []
            : permissions.Distinct(StringComparer.Ordinal);
}
