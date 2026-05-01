using Microsoft.EntityFrameworkCore;
using Skinora.Admin.Domain.Entities;
using Skinora.Auth.Configuration;
using Skinora.Shared.Persistence;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// EF Core-backed resolver that joins <see cref="AdminUserRole"/> →
/// <see cref="AdminRole"/> → <see cref="AdminRolePermission"/>. Soft-deleted
/// rows are filtered automatically by the global query filters configured in
/// <c>AdminRoleConfiguration</c> / <c>AdminUserRoleConfiguration</c> /
/// <c>AdminRolePermissionConfiguration</c>.
/// </summary>
public sealed class AdminAuthorityResolver : IAdminAuthorityResolver
{
    private readonly AppDbContext _db;

    public AdminAuthorityResolver(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AdminAuthority> ResolveAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var assignment = await (
            from ur in _db.Set<AdminUserRole>().AsNoTracking()
            join r in _db.Set<AdminRole>().AsNoTracking() on ur.AdminRoleId equals r.Id
            where ur.UserId == userId
            select new { r.Id, r.IsSuperAdmin })
            .FirstOrDefaultAsync(cancellationToken);

        if (assignment is null)
        {
            return new AdminAuthority(AuthRoles.User, []);
        }

        if (assignment.IsSuperAdmin)
        {
            // PermissionAuthorizationHandler bypasses every requirement when
            // role = super_admin, so emitting individual permission claims is
            // unnecessary (and would inflate the JWT for no benefit).
            return new AdminAuthority(AuthRoles.SuperAdmin, []);
        }

        var permissions = await _db.Set<AdminRolePermission>()
            .AsNoTracking()
            .Where(p => p.AdminRoleId == assignment.Id)
            .OrderBy(p => p.Permission)
            .Select(p => p.Permission)
            .ToListAsync(cancellationToken);

        return new AdminAuthority(AuthRoles.Admin, permissions);
    }
}
