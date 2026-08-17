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
    // Best-effort audit field: only populated when inventory evidence could be
    // produced. A delivery closed by buyer confirmation alone leaves this null,
    // so it is NOT a state guard — DeliveryEvidence is (06 §8.4).
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
    public DateTime? SellerConfirmDeadline { get; set; }
    public DateTime? PaymentDeadline { get; set; }

    // The seller's window to send the item directly to the buyer. This is the
    // only time bound on seller non-delivery, so unlike the old trade-offer
    // deadlines it is actually armed on entry to PAYMENT_RECEIVED (05 §4.4).
    public DateTime? DeliveryDeadline { get; set; }
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

    // --- Delivery Verification (02 §9.2) ---
    // The platform is not a party to the seller->buyer trade and cannot see its
    // receipt, so delivery is proven by observation instead: the buyer's own
    // confirmation, or the pair (seller's asset gone) AND (buyer's class count up).
    public string? BuyerTradeUrl { get; set; }
    public DateTime? SellerReadyConfirmedAt { get; set; }

    // Baseline captured on entry to SELLER_CONFIRMED — deliberately not at
    // payment time. A seller can technically send before paying; a later
    // baseline would absorb that item and the delta would never register.
    public int? BuyerBaselineClassCount { get; set; }
    public string? BuyerBaselineAssetIds { get; set; }

    // T130 — the buyer's WHOLE inventory as a distinct class-id set, captured in
    // the same read as the two columns above. The class-scoped baseline cannot
    // see an arrival of a different class, and a wrong item is by definition a
    // different class: without an inventory-wide reference point 02 §10.1's
    // "gelen item'ın adı kayda geçirilerek admin'e yükseltilir" has nothing to
    // name. NULL alongside a NULL BuyerBaselineCapturedAt — same read, same gap.
    public string? BuyerBaselineClassIds { get; set; }

    // Null when the buyer's inventory was unreadable. This does not block the
    // transaction — it only closes the evidence path, leaving buyer
    // confirmation as the sole route (02 §9.2).
    public DateTime? BuyerBaselineCapturedAt { get; set; }

    public DateTime? BuyerConfirmedReceiptAt { get; set; }
    public DateTime? DeliveryVerifiedAt { get; set; }
    public DeliveryEvidence DeliveryEvidence { get; set; }

    // When the delivery-timeout verification round last EXAMINED this row —
    // written on every round, including the ones that conclude nothing. Three
    // of the five verdicts leave the row PAYMENT_RECEIVED and permanently
    // overdue, so the scanner cannot order its (rate-limit bounded) window by
    // deadline alone: the same oldest rows would refill it forever and a
    // delivery that expired today would never get a round. Ordering by this
    // column, nulls first, makes the queue fair by construction (T127).
    public DateTime? DeliveryRoundAt { get; set; }

    // --- Settlement (02 §4.5.1) ---
    // Steam lets either side reverse a protected trade for 7 days, with no
    // Steam Support involvement. Paying out before that window closes would let
    // a seller take the money and then pull the item back. So payout waits —
    // and, critically, re-checks that the item is still with the buyer before
    // releasing. Waiting alone does not protect; the check at the end does.
    public DateTime? PayoutEligibleAt { get; set; }
    public DateTime? SettlementVerifiedAt { get; set; }
    public DateTime? DeliveryReversedAt { get; set; }

    // When the settlement re-check last EXAMINED this row (T129) — the twin of
    // DeliveryRoundAt above, and for the same reason: a row whose inventory
    // cannot be read stays eligible forever, so ordering the batch by
    // PayoutEligibleAt alone would let the oldest unreadable rows refill the
    // window and starve every settlement that came due today.
    public DateTime? SettlementCheckedAt { get; set; }

    // When the settlement re-check handed this row to an admin — an inventory
    // that stayed unreadable past settlement.unreadable_escalation_hours, an
    // item that left the buyer without proof of a reversal, or a reversal
    // signature raised while the auto-refund gate is closed. Idempotency
    // marker: the admin is told once, not once per tick.
    public DateTime? SettlementEscalatedAt { get; set; }

    // WHY it was handed over, as the machine-readable SettlementReviewReasons
    // code. Two jobs, both added by the T129 fix round:
    //  * the reason lived only in the outbox event, so a triage query could not
    //    tell an unreadable inventory from a delivery that left NO reference to
    //    measure against — two classes whose procedures differ (DEPLOY_RUNBOOK
    //    §I.3 vs §I.5);
    //  * it makes the escalation STICKY. A round that watched the item LEAVE the
    //    buyer must not be undone by a later round that happens to read it back:
    //    without this column ClearForPayout stamped the clearance regardless and
    //    the money left while an ADMIN_ESCALATION sat open, unannounced.
    public string? SettlementEscalationReason { get; set; }

    // Set when an admin closed the settlement in the seller's favour instead of
    // the check concluding on its own (07 §9.22b). Evidentiary only — no money
    // gate reads it; SettlementVerifiedAt is still the single column that opens
    // payout, sweep and COMPLETED. It exists so a row cleared by a human is
    // distinguishable from one cleared by the job, in the triage query and in
    // the audit trail alike.
    public Guid? SettlementClearedByAdminId { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // --- Milestone Timestamps ---
    public DateTime? AcceptedAt { get; set; }
    public DateTime? PaymentReceivedAt { get; set; }
    public DateTime? ItemDeliveredAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    // --- Navigation properties ---
    public ICollection<TransactionHistory> History { get; set; } = [];
    public PaymentAddress? PaymentAddress { get; set; }
    public ICollection<BlockchainTransaction> BlockchainTransactions { get; set; } = [];
}
