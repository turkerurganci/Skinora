using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Domain.Entities;

/// <summary>
/// Unique blockchain payment address generated per transaction.
/// All fields per 06 §3.7.
/// </summary>
public class PaymentAddress : BaseEntity, ISoftDeletable
{
    public Guid TransactionId { get; set; }

    /// <summary>
    /// The network this deposit address lives on (06 §1.4, T145). Equals the
    /// transaction's network; the HD index is counted per network, so
    /// uniqueness is <c>(Network, Address)</c> and <c>(Network, HdWalletIndex)</c>.
    /// </summary>
    public BlockchainNetwork Network { get; set; } = BlockchainNetwork.TRON;

    public string Address { get; set; } = string.Empty;
    public int HdWalletIndex { get; set; }
    public decimal ExpectedAmount { get; set; }
    public StablecoinType ExpectedToken { get; set; }
    public MonitoringStatus MonitoringStatus { get; set; }
    public DateTime? MonitoringExpiresAt { get; set; }

    /// <summary>
    /// T158 (h) — "watch from this block" (06 §3.7, 08 §3.7.4): the network
    /// service's last <c>finalized</c> block when it derived the address.
    /// Nothing can reach a new address at or before it, so every monitor
    /// start sends it and the service reads the deposit from the next block
    /// with a zero balance baseline — however long the service was down, past
    /// its bounded restart read-back. NULL on TRON (its service does not
    /// report one) and on rows allocated before T158.
    /// </summary>
    public long? MonitorFromBlock { get; set; }

    /// <summary>
    /// T160c — earliest UTC instant <c>RefundFeeSweepQueueJob</c> looks at
    /// this deposit's uncollected refund fees again. NULL = at the next run.
    /// Set when collecting would cost more than it brings (or the cost could
    /// not be priced), so a deposit whose fees are not worth moving yet is not
    /// re-priced every run.
    /// </summary>
    public DateTime? RefundFeeSweepNextCheckAt { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // --- Navigation properties ---
    public Transaction Transaction { get; set; } = null!;
    public ICollection<BlockchainTransaction> BlockchainTransactions { get; set; } = [];
}
