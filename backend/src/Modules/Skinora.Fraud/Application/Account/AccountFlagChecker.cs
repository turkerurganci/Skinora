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

    public async Task<bool> HasPendingAccountFlagAsync(
        Guid userId,
        FraudFlagType type,
        CancellationToken cancellationToken)
    {
        // Staged-but-unsaved first (interface remarks): the AsNoTracking query
        // below cannot see a flag added earlier in the same unit of work.
        if (_db.Set<FraudFlag>().Local.Any(f => IsPendingAccountFlag(f, userId, type)))
            return true;

        return await _db.Set<FraudFlag>()
            .AsNoTracking()
            .AnyAsync(
                f => f.UserId == userId
                     && f.Type == type
                     && f.Scope == FraudFlagScope.ACCOUNT_LEVEL
                     && f.Status == ReviewStatus.PENDING
                     && !f.IsDeleted,
                cancellationToken);
    }

    private static bool IsPendingAccountFlag(FraudFlag flag, Guid userId, FraudFlagType type) =>
        flag.UserId == userId
        && flag.Type == type
        && flag.Scope == FraudFlagScope.ACCOUNT_LEVEL
        && flag.Status == ReviewStatus.PENDING
        && !flag.IsDeleted;
}
