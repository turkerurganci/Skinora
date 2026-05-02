using Microsoft.EntityFrameworkCore;
using Skinora.Fraud.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Lifecycle;

namespace Skinora.Fraud.Application.Account;

/// <summary>
/// EF Core-backed implementation of <see cref="IAccountFlagChecker"/>. Lives
/// here (not in <c>Skinora.Transactions</c>) because Fraud already references
/// Transactions; reversing the direction would create a project cycle.
/// </summary>
public sealed class AccountFlagChecker : IAccountFlagChecker
{
    private readonly AppDbContext _db;

    public AccountFlagChecker(AppDbContext db)
    {
        _db = db;
    }

    public Task<bool> HasActiveAccountFlagAsync(Guid userId, CancellationToken cancellationToken)
        => _db.Set<FraudFlag>()
            .AsNoTracking()
            .AnyAsync(
                f => f.UserId == userId
                     && f.Scope == FraudFlagScope.ACCOUNT_LEVEL
                     && f.Status != ReviewStatus.REJECTED
                     && !f.IsDeleted,
                cancellationToken);
}
