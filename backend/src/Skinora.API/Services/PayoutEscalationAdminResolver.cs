using Microsoft.EntityFrameworkCore;
using Skinora.Admin.Domain.Entities;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.PayoutIssues;

namespace Skinora.API.Services;

/// <summary>
/// Production <see cref="IPayoutEscalationAdminResolver"/> — picks the
/// earliest-assigned non-deleted admin from <c>AdminUserRole</c>. Lives at
/// the API composition root because <c>Skinora.Transactions</c> cannot
/// reference <c>Skinora.Admin</c> (would be a project cycle peer to the
/// existing Disputes/Fraud convention).
/// </summary>
/// <remarks>
/// The "earliest-assigned" tiebreaker is intentionally simple — admins
/// rotate through escalations via T63's admin dashboard once it ships, and
/// per-issue load balancing is owned there, not here. Returning <c>null</c>
/// is allowed (e.g. a pristine deployment with no admin assignments yet);
/// the calling service surfaces the error explicitly rather than silently
/// dropping the escalation.
/// </remarks>
public sealed class PayoutEscalationAdminResolver : IPayoutEscalationAdminResolver
{
    private readonly AppDbContext _db;

    public PayoutEscalationAdminResolver(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Guid?> ResolveAdminUserIdAsync(CancellationToken cancellationToken)
    {
        return await _db.Set<AdminUserRole>()
            .AsNoTracking()
            .OrderBy(r => r.AssignedAt)
            .Select(r => (Guid?)r.UserId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
