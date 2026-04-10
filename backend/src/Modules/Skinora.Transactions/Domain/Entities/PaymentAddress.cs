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
    public string Address { get; set; } = string.Empty;
    public int HdWalletIndex { get; set; }
    public decimal ExpectedAmount { get; set; }
    public StablecoinType ExpectedToken { get; set; }
    public MonitoringStatus MonitoringStatus { get; set; }
    public DateTime? MonitoringExpiresAt { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // --- Navigation properties ---
    public Transaction Transaction { get; set; } = null!;
    public ICollection<BlockchainTransaction> BlockchainTransactions { get; set; } = [];
}
