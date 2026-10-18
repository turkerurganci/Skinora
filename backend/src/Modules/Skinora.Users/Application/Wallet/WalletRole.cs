using Skinora.Shared.Enums;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Which side of a transaction a wallet address belongs to
/// (02 §12.1 / §12.2 — seller payout vs buyer refund).
/// </summary>
public enum WalletRole
{
    /// <summary>Satıcı ödeme adresi — <see cref="WalletAddressType.PAYOUT"/>.</summary>
    Seller,

    /// <summary>Alıcı iade adresi — <see cref="WalletAddressType.REFUND"/>.</summary>
    Buyer,
}

public static class WalletRoleExtensions
{
    /// <summary>The <see cref="UserWalletAddress.Type"/> a role's address is stored under.</summary>
    public static WalletAddressType ToAddressType(this WalletRole role)
        => role == WalletRole.Seller ? WalletAddressType.PAYOUT : WalletAddressType.REFUND;

    /// <summary>The <see cref="WalletAddressHistory.Type"/> wire value (07 §9.16) for a role.</summary>
    public static string ToHistoryType(this WalletRole role)
        => role == WalletRole.Seller ? WalletAddressHistoryType.Seller : WalletAddressHistoryType.Buyer;
}
