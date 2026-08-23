using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Domain;
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

        // Non-terminal statuses per 02 §12.3 snapshot principle. Terminals no
        // longer "use" the address operationally. REFUNDED counts as terminal
        // here as of the T133a-ActiveCounterRefunded fix: before it, a user
        // whose only open row was a buyer-favour refund could not change their
        // wallet address at all (02 §12.3).
        return await query
            .Where(t => !TransactionStatusSets.Terminal.Contains(t.Status))
            .CountAsync(cancellationToken);
    }
}
