using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Domain;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Application.Account;

namespace Skinora.Transactions.Application.Account;

/// <summary>
/// EF Core-backed implementation of
/// <see cref="IUserActiveTransactionChecker"/>. Placed in
/// <c>Skinora.Transactions</c> (mirrors
/// <see cref="Skinora.Transactions.Application.Wallet.ActiveTransactionCounter"/>
/// from T34) so <c>Skinora.Users</c> stays free of a Transactions reference.
/// </summary>
public sealed class UserActiveTransactionChecker : IUserActiveTransactionChecker
{
    private readonly AppDbContext _db;

    public UserActiveTransactionChecker(AppDbContext db)
    {
        _db = db;
    }

    public Task<bool> HasActiveTransactionsAsync(
        Guid userId, CancellationToken cancellationToken)
        => _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.BuyerId == userId || t.SellerId == userId)
            // REFUNDED counts as terminal as of the T133a-ActiveCounterRefunded
            // fix: before it, a user whose only open row was a buyer-favour
            // refund could not close their account at all (02 §19).
            .Where(t => !TransactionStatusSets.Terminal.Contains(t.Status))
            .AnyAsync(cancellationToken);
}
