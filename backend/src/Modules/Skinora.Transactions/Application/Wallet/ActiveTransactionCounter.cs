using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Application.Wallet;

namespace Skinora.Transactions.Application.Wallet;

/// <summary>
/// EF Core-backed implementation of <see cref="IActiveTransactionCounter"/>.
/// Placed here (not in <c>Skinora.Users</c>) because <c>Transactions</c>
/// already depends on <c>Users</c>; reversing the direction would create a
/// cycle. The API composition root wires this into the DI container.
/// </summary>
public sealed class ActiveTransactionCounter : IActiveTransactionCounter
{
    private readonly AppDbContext _db;

    public ActiveTransactionCounter(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> CountActiveUsingAddressAsync(
        Guid userId,
        WalletRole role,
        string previousAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(previousAddress)) return 0;

        var query = _db.Set<Transaction>().AsNoTracking();

        query = role switch
        {
            WalletRole.Seller => query.Where(t =>
                t.SellerId == userId &&
                t.SellerPayoutAddress == previousAddress),
            WalletRole.Buyer => query.Where(t =>
                t.BuyerId == userId &&
                t.BuyerRefundAddress != null &&
                t.BuyerRefundAddress == previousAddress),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

        // Non-terminal statuses per 02 §12.3 snapshot principle. Terminals
        // (COMPLETED, CANCELLED_*) no longer "use" the address operationally.
        return await query
            .Where(t =>
                t.Status != TransactionStatus.COMPLETED &&
                t.Status != TransactionStatus.CANCELLED_TIMEOUT &&
                t.Status != TransactionStatus.CANCELLED_SELLER &&
                t.Status != TransactionStatus.CANCELLED_BUYER &&
                t.Status != TransactionStatus.CANCELLED_ADMIN)
            .CountAsync(cancellationToken);
    }
}
