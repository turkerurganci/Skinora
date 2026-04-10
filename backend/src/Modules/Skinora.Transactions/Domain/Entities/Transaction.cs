using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Domain.Entities;

/// <summary>
/// Central entity for the transaction lifecycle. Contains item snapshot, price,
/// status, parties, timeout and emergency hold info.
/// All fields per 06 §3.5.
/// </summary>
public class Transaction : BaseEntity, ISoftDeletable, IAuditableEntity
{
    // --- Status ---
    public TransactionStatus Status { get; set; }

    // --- Parties ---
    public Guid SellerId { get; set; }
    public Guid? BuyerId { get; set; }
    public BuyerIdentificationMethod BuyerIdentificationMethod { get; set; }
    public string? TargetBuyerSteamId { get; set; }
    public string? InviteToken { get; set; }

    // --- Item Snapshot ---
    public string ItemAssetId { get; set; } = string.Empty;
    public string ItemClassId { get; set; } = string.Empty;
    public string? ItemInstanceId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? ItemIconUrl { get; set; }
    public string? ItemExterior { get; set; }
    public string? ItemType { get; set; }
    public string? ItemInspectLink { get; set; }

    // --- Item Asset Lineage ---
    public string? EscrowBotAssetId { get; set; }
    public string? DeliveredBuyerAssetId { get; set; }

    // --- Price & Commission ---
    public StablecoinType StablecoinType { get; set; }
    public decimal Price { get; set; }
    public decimal CommissionRate { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal? MarketPriceAtCreation { get; set; }

    // --- Wallet Addresses (Snapshot) ---
    public string SellerPayoutAddress { get; set; } = string.Empty;
    public string? BuyerRefundAddress { get; set; }

    // --- Timeout ---
    public int PaymentTimeoutMinutes { get; set; }
    public DateTime? AcceptDeadline { get; set; }
    public DateTime? TradeOfferToSellerDeadline { get; set; }
    public DateTime? PaymentDeadline { get; set; }
    public DateTime? TradeOfferToBuyerDeadline { get; set; }
    public DateTime? TimeoutFrozenAt { get; set; }
    public TimeoutFreezeReason? TimeoutFreezeReason { get; set; }
    public int? TimeoutRemainingSeconds { get; set; }

    // --- Emergency Hold ---
    public bool IsOnHold { get; set; }
    public DateTime? EmergencyHoldAt { get; set; }
    public string? EmergencyHoldReason { get; set; }
    public Guid? EmergencyHoldByAdminId { get; set; }
    public int? PreviousStatusBeforeHold { get; set; }

    // --- Hangfire Job IDs ---
    public string? PaymentTimeoutJobId { get; set; }
    public string? TimeoutWarningJobId { get; set; }
    public DateTime? TimeoutWarningSentAt { get; set; }

    // --- Cancellation ---
    public CancelledByType? CancelledBy { get; set; }
    public string? CancelReason { get; set; }

    // --- Dispute ---
    public bool HasActiveDispute { get; set; }

    // --- Steam Bot ---
    public Guid? EscrowBotId { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // --- Milestone Timestamps ---
    public DateTime? AcceptedAt { get; set; }
    public DateTime? ItemEscrowedAt { get; set; }
    public DateTime? PaymentReceivedAt { get; set; }
    public DateTime? ItemDeliveredAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    // --- Navigation properties ---
    public ICollection<TransactionHistory> History { get; set; } = [];
    public PaymentAddress? PaymentAddress { get; set; }
    public ICollection<BlockchainTransaction> BlockchainTransactions { get; set; } = [];
}
