namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Which side of a transaction a wallet address belongs to
/// (02 §12.1 / §12.2 — seller payout vs buyer refund).
/// </summary>
public enum WalletRole
{
    /// <summary>Satıcı ödeme adresi — <c>DefaultPayoutAddress</c>.</summary>
    Seller,

    /// <summary>Alıcı iade adresi — <c>DefaultRefundAddress</c>.</summary>
    Buyer,
}
