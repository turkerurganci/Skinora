namespace Skinora.Shared.Enums;

/// <summary>
/// Which of a user's two saved addresses a <c>UserWalletAddress</c> row holds
/// (06 §2.26, §1.4 — T146). A user has at most one of each per network.
/// </summary>
public enum WalletAddressType
{
    /// <summary>Seller payout address — where the seller's money is sent (02 §12.1).</summary>
    PAYOUT,

    /// <summary>Buyer refund address — the target of every refund (02 §12.2, 11 §F8 K9).</summary>
    REFUND,
}
