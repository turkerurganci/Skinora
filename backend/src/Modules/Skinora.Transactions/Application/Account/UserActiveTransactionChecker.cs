using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
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
            .Where(t =>
                t.Status != TransactionStatus.COMPLETED &&
                t.Status != TransactionStatus.CANCELLED_TIMEOUT &&
                t.Status != TransactionStatus.CANCELLED_SELLER &&
                t.Status != TransactionStatus.CANCELLED_BUYER &&
                t.Status != TransactionStatus.CANCELLED_ADMIN)
            .AnyAsync(cancellationToken);
}
