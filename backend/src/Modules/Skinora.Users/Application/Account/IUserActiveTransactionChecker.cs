namespace Skinora.Users.Application.Account;

/// <summary>
/// Ports the "does the user have any non-terminal transaction?" check used
/// by <see cref="IAccountLifecycleService"/> to enforce 02 §19 and 07 §5.17
/// — accounts with active transactions cannot be deactivated or deleted.
/// </summary>
/// <remarks>
/// "Active" means <c>Status &#8712; { CREATED, ACCEPTED,
/// TRADE_OFFER_SENT_TO_SELLER, ITEM_ESCROWED, PAYMENT_RECEIVED,
/// TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED, FLAGGED }</c> — i.e. any
/// non-terminal state on either the buyer or seller side. Terminals are
/// <c>COMPLETED</c> and all <c>CANCELLED_*</c> variants.
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
