namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Ports the "active transactions using old wallet address" count (07 §5.3
/// response) without creating a <c>Skinora.Users &#8594; Skinora.Transactions</c>
/// reference. The real implementation lives in the Transactions module and is
/// registered by the API composition root.
/// </summary>
/// <remarks>
/// "Active" means <c>Status &#8712; { CREATED, ACCEPTED, TRADE_OFFER_SENT_TO_SELLER,
/// ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED,
/// FLAGGED }</c> — i.e. every non-terminal state. Terminal states are
/// <c>COMPLETED</c> and all <c>CANCELLED_*</c> variants. 02 §12.3 snapshot
/// principle: the count reflects pre-change addresses still in flight.
/// </remarks>
public interface IActiveTransactionCounter
{
    /// <summary>
    /// Counts non-terminal transactions where the given user still has
    /// <paramref name="previousAddress"/> snapshotted on the role-specific
    /// address column.
    /// </summary>
    Task<int> CountActiveUsingAddressAsync(
        Guid userId,
        WalletRole role,
        string previousAddress,
        CancellationToken cancellationToken);
}
