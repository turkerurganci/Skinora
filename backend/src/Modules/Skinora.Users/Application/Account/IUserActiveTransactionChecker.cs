namespace Skinora.Users.Application.Account;

/// <summary>
/// Ports the "does the user have any non-terminal transaction?" check used
/// by <see cref="IAccountLifecycleService"/> to enforce 02 §19 and 07 §5.17
/// — accounts with active transactions cannot be deactivated or deleted.
/// </summary>
/// <remarks>
/// "Active" means non-terminal on either the buyer or the seller side. The
/// query expresses this as an exclusion, so it matches <c>CREATED</c>,
/// <c>ACCEPTED</c>, <c>SELLER_CONFIRMED</c>, <c>PAYMENT_RECEIVED</c>,
/// <c>ITEM_DELIVERED</c> and <c>FLAGGED</c> — and, as the exclusion list stands
/// today, also the terminal <c>REFUNDED</c>, which is not named there.
/// Excluded: <c>COMPLETED</c> and all <c>CANCELLED_*</c> variants.
/// <para>
/// The abstraction lives here (and the implementation in
/// <c>Skinora.Transactions</c>) so <c>Skinora.Users</c> does not depend on
/// <c>Skinora.Transactions</c>. Mirrors the
/// <see cref="Skinora.Users.Application.Wallet.IActiveTransactionCounter"/>
/// split from T34.
/// </para>
/// </remarks>
public interface IUserActiveTransactionChecker
{
    Task<bool> HasActiveTransactionsAsync(Guid userId, CancellationToken cancellationToken);
}
