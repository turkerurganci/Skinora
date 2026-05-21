using Skinora.Shared.Models;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// T1 — <c>GET /transactions</c> (07 §7.1, T83a). Returns the caller's
/// own transactions filtered by tab (active / completed / cancelled),
/// newest first, with the EMERGENCY_HOLD overlay projected onto the
/// status string.
/// </summary>
public interface ITransactionListService
{
    Task<PagedResult<TransactionListItemDto>> ListAsync(
        Guid callerId, TransactionListQuery query, CancellationToken cancellationToken);
}
