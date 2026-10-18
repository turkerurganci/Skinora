using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Domain.Entities;

/// <summary>
/// Seller-reported payout problem and its resolution trail. All fields per
/// 06 §3.8a (02 §10.3).
/// </summary>
/// <remarks>
/// Workflow Record (Arşivlenebilir): DELETE is not defined. The row is
/// updated through its verification states and frozen once it reaches
/// <c>RESOLVED</c>. At most one active (non-<c>RESOLVED</c>) issue is
/// permitted per transaction; enforcement is a filtered unique index
/// (06 §5.1).
/// </remarks>
public class SellerPayoutIssue : BaseEntity, IAuditableEntity
{
    public Guid TransactionId { get; set; }
    public Guid SellerId { get; set; }

    /// <summary>
    /// Network the disputed payout was sent on — the transaction's network
    /// (06 §1.4, T145). <see cref="PayoutTxHash"/> is only meaningful on it.
    /// </summary>
    public BlockchainNetwork Network { get; set; } = BlockchainNetwork.TRON;

    public string Detail { get; set; } = string.Empty;
    public string? PayoutTxHash { get; set; }
    public PayoutIssueStatus VerificationStatus { get; set; }
    public int RetryCount { get; set; }
    public Guid? EscalatedToAdminId { get; set; }
    public string? AdminNote { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
