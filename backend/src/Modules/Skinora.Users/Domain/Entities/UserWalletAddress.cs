using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Users.Domain.Entities;

/// <summary>
/// A user's saved payout or refund address on one network (06 §1.4, §3.1a —
/// T146). At most one row per <c>(UserId, Network, Type)</c>; the row is
/// updated in place when the address changes and the superseded value is
/// appended to <see cref="WalletAddressHistory"/>.
/// </summary>
/// <remarks>
/// Every rule of 02 §12 — re-authentication on change, the
/// <c>wallet.*_cooldown_hours</c> window, sanctions screening, the snapshot
/// principle — applies per row: changing the Ethereum payout address does not
/// start the TRON payout cooldown. Replaces the pre-F8
/// <c>User.DefaultPayoutAddress</c> / <c>DefaultRefundAddress</c> /
/// <c>PayoutAddressChangedAt</c> / <c>RefundAddressChangedAt</c> columns
/// (migrated to <c>Network = TRON</c> rows by <c>T146_UserWalletAddresses</c>).
/// </remarks>
public class UserWalletAddress : BaseEntity, IAuditableEntity
{
    public Guid UserId { get; set; }

    public BlockchainNetwork Network { get; set; }

    public WalletAddressType Type { get; set; }

    /// <summary>Canonical form (06 §1.4): EVM lowercase, TRON base58 unchanged.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// When the address was last set — the start of the 02 §12.3 cooldown
    /// window for this network and type. Every write through the wallet
    /// endpoints sets it; <c>null</c> only on a row migrated from an address
    /// that predates change tracking (T34), which carries no cooldown.
    /// </summary>
    public DateTime? ChangedAt { get; set; }

    // --- Navigation ---
    public User User { get; set; } = null!;

    /// <summary>
    /// A new row for <paramref name="network"/> / <paramref name="type"/>, to be
    /// attached to a user (through <see cref="User.WalletAddresses"/> or by
    /// setting <see cref="UserId"/>). The address must already be validated and
    /// canonical — this factory does not check it.
    /// </summary>
    public static UserWalletAddress For(
        BlockchainNetwork network, WalletAddressType type, string address, DateTime? changedAt = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Network = network,
            Type = type,
            Address = address,
            ChangedAt = changedAt,
        };
}
