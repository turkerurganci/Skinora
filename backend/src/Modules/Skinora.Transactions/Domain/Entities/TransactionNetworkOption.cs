using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Domain.Entities;

/// <summary>
/// One network a listing accepts, with the seller's payout address on that
/// network fixed at creation (06 §1.4, §3.5a — T147; 11 §F8 K3).
/// </summary>
/// <remarks>
/// <para>
/// The seller picks the networks a listing accepts among those with a saved
/// payout address; one row is written per network when the transaction is
/// created and the address is the 02 §12.3 snapshot: a later change of the
/// seller's profile address does not move an open listing's payout. When the
/// buyer accepts, <see cref="Transaction.Network"/> is set to one of these
/// networks and <see cref="Transaction.SellerPayoutAddress"/> is copied from
/// its row.
/// </para>
/// <para>
/// Append-only: the snapshot never changes after creation.
/// </para>
/// </remarks>
public class TransactionNetworkOption : IAppendOnly
{
    public Guid Id { get; set; }

    public Guid TransactionId { get; set; }

    public BlockchainNetwork Network { get; set; }

    /// <summary>Canonical form (06 §1.4): EVM lowercase, TRON base58 unchanged.</summary>
    public string SellerPayoutAddress { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // --- Navigation ---
    public Transaction Transaction { get; set; } = null!;
}
