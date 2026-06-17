using Microsoft.EntityFrameworkCore;
using Skinora.Admin.Domain.Entities;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;

namespace Skinora.Admin.Application.Notifications;

/// <summary>
/// Default <see cref="IAdminRecipientResolver"/> (WP8) — returns every user
/// holding at least one active admin-role assignment, the broadcast audience
/// for admin-targeted in-app notifications.
/// </summary>
/// <remarks>
/// The <see cref="AdminUserRole"/> soft-delete query filter already hides
/// tombstoned assignments, so a user stripped of their last role drops out of
/// the result set automatically. <c>Distinct</c> collapses the N:M fan-out
/// (a user with several roles is notified once).
/// </remarks>
public sealed class AdminRecipientResolver : IAdminRecipientResolver
{
    private readonly AppDbContext _db;

    public AdminRecipientResolver(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> GetAdminUserIdsAsync(CancellationToken cancellationToken)
    {
        return await _db.Set<AdminUserRole>()
            .Select(r => r.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
