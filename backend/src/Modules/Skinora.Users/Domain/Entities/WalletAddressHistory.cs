using Skinora.Shared.Domain;

namespace Skinora.Users.Domain.Entities;

/// <summary>
/// Append-only history of replaced payout/refund wallet addresses
/// (T105b — 06 §3.1, §4.2; 04 §8.9.3). One row is written each time a user
/// replaces an existing wallet address; it records the address that was just
/// superseded and the date that address had originally been set. The current
/// addresses continue to live on <see cref="User"/> (DefaultPayoutAddress /
/// DefaultRefundAddress) — they are not duplicated here. Immutability is
/// enforced at the <c>AppDbContext</c> level via <see cref="IAppendOnly"/>
/// (06 §4.2), so "current" is derived from the <see cref="User"/> row at read
/// time and never stored as a mutable flag.
/// </summary>
public class WalletAddressHistory : IAppendOnly
{
    public long Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>
    /// Which address was replaced: <c>"seller"</c> (payout) or <c>"buyer"</c>
    /// (refund) — the wire value of AD16 <c>walletHistory[].type</c>
    /// (07 §9.16). Mirrors <c>AdminWalletEntryType</c> in Skinora.Admin.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The previous wallet address (the value that was superseded).</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// When the superseded address had originally been set — copied from
    /// <c>User.PayoutAddressChangedAt</c> / <c>RefundAddressChangedAt</c>
    /// before the overwrite. Null only for an address that predates change
    /// tracking (T34).
    /// </summary>
    public DateTime? SetAt { get; set; }

    /// <summary>
    /// When this history row was recorded (= the replacement moment). Immutable.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    // --- Navigation ---
    public User User { get; set; } = null!;
}

/// <summary>Stable <see cref="WalletAddressHistory.Type"/> values (07 §9.16).</summary>
public static class WalletAddressHistoryType
{
    public const string Seller = "seller";
    public const string Buyer = "buyer";
}
