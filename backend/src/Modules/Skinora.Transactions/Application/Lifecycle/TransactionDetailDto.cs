using System.Text.Json.Serialization;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Top-level response for <c>GET /transactions/:id</c> (07 §7.5). Carries
/// every field listed in the authenticated contract; sections that depend
/// on a state not yet reachable in the current implementation phase
/// (payment, sellerPayout, refund, cancelInfo, holdInfo, dispute, etc.)
/// are emitted as <c>null</c> via <c>WhenWritingNull</c> until the owning
/// task ships (T47/T49/T51/T54/T58/T59/T70+). Public callers receive a
/// trimmed shape: only the fields permitted by the public sample stay
/// non-null.
/// </summary>
public sealed record TransactionDetailDto(
    Guid Id,
    // 07 §7.1/§7.5 — projected status string, not the raw TransactionStatus
    // enum: an EMERGENCY_HOLD (IsOnHold) overlay is surfaced over the real
    // state so the FE hold banner + frozen action panel fire (04 §7.3,
    // 06 §2.20). Mirrors TransactionListItemDto.Status, which is already a
    // projected string for the same reason.
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UserRole,
    TransactionItemDto Item,
    string Price,
    StablecoinType Stablecoin,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? CommissionRate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CommissionAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TotalAmount,
    TransactionPartyDto Seller,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TransactionPartyDto? Buyer,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TransactionTimeoutDto? Timeout,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TransactionPaymentDto? Payment,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SellerPayoutDto? SellerPayout,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RefundDto? Refund,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CancelInfoDto? CancelInfo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FlagInfoDto? FlagInfo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] HoldInfoDto? HoldInfo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DisputeSummaryDto? Dispute,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] InviteInfoDto? InviteInfo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PaymentEventDto>? PaymentEvents,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeliveredBuyerAssetId,
    // WP12 (T90 K3) — the buyer's own Steam trade URL, surfaced only to the
    // seller in PAYMENT_RECEIVED (04 §7.3 "Steam'e git linki"): the CTA that
    // opens the trade the seller must send. The platform creates no trade offer
    // of its own (02 §2.2 step 6). Null in every other state, for the buyer's
    // view, and in the public/trimmed shape. The property name predates the
    // v3.0 pivot and is kept because it is part of the 07 §7.5 JSON contract.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SteamTradeOfferUrl,
    // T135 — whether the delivery baseline could be taken when the seller
    // confirmed readiness (03 §2.3 step 3). The same fact 07 §7.6a returns once,
    // in the confirm-ready response; persisted here because it stays true for
    // the rest of the transaction and BOTH parties act on it: false means the
    // 02 §9.2 inventory-evidence path is closed and delivery can only be proven
    // by the buyer pressing "Teslim Aldım" (04 §7.3 ACCEPTED note, 03 §3.5
    // note). Surfacing it only in the one-shot confirm-ready reply would put a
    // standing obligation behind a message that disappears on the next reload —
    // and the buyer, who carries that obligation, never sees that reply at all.
    //
    // Null (field suppressed) means "not known yet", not "visible": before the
    // seller confirms, the buyer's inventory has never been read. The gate is
    // the milestone stamp, not a status name — same rule the sibling `payment`
    // block uses (07 §7.5), so the answer survives a later cancellation.
    // Party-only: null on the public / prospective-buyer surface.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? BuyerInventoryVisible,
    AvailableActionsDto AvailableActions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? UpdatedAt);

/// <summary>Item snapshot for the detail response (07 §7.5).</summary>
public sealed record TransactionItemDto(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AssetId,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ImageUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Wear);

/// <summary>Party block (seller / buyer) for the detail response (07 §7.5).</summary>
public sealed record TransactionPartyDto(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SteamId,
    string DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AvatarUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? ReputationScore,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? CompletedTransactionCount);

/// <summary>Active timeout block for the detail response (07 §7.5).</summary>
public sealed record TransactionTimeoutDto(
    string Type,
    DateTime ExpiresAt,
    int RemainingSeconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? WarningThresholdPercent,
    bool Frozen,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FrozenReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? FrozenAt);

/// <summary>Payment block; populated from <c>SELLER_CONFIRMED</c> onwards (07 §7.5).</summary>
public sealed record TransactionPaymentDto(
    string Address,
    string ExpectedAmount,
    StablecoinType Stablecoin,
    string Network,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TxHash,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? ConfirmedAt);

/// <summary>Seller payout block; populated in <c>COMPLETED</c> for the seller view.</summary>
public sealed record SellerPayoutDto(
    string GrossAmount,
    string GasFee,
    string GasFeeFromCommission,
    string GasFeeFromSeller,
    string NetAmount,
    string WalletAddress,
    string TxHash,
    DateTime SentAt);

/// <summary>Refund block; populated when a payment refund is issued.</summary>
public sealed record RefundDto(
    string OriginalAmount,
    string GasFee,
    string NetRefundAmount,
    string RefundAddress,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TxHash,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? RefundedAt);

/// <summary>Cancellation block (07 §7.5 cancelInfo).</summary>
// ItemReturned removed in v3.0: the platform never holds the item, so there is
// no item to return on cancellation (02 §9).
public sealed record CancelInfoDto(
    string CancelledBy,
    string Reason,
    DateTime CancelledAt,
    bool PaymentRefunded,
    // WP2c — the status the transaction held when it was cancelled, so the
    // timeline can put its red X on the step the flow actually stopped at
    // (04 §C05) instead of always on step 1. Same concept as
    // HoldInfoDto.PreviousStatus.
    //
    // Derived from TransactionHistory.PreviousStatus rather than stored on the
    // Transaction row — the transition is already recorded, so no column and no
    // migration are needed. Null when no history row is found (a pre-history
    // record); the client keeps its old behaviour in that case.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StatusAtCancellation);

/// <summary>Flag info block (07 §7.5 flagInfo).</summary>
public sealed record FlagInfoDto(string FlagType, string Message);

/// <summary>Emergency hold block (07 §7.5 holdInfo).</summary>
public sealed record HoldInfoDto(
    string PreviousStatus,
    string Reason,
    DateTime FrozenAt,
    string Message);

/// <summary>Dispute summary block (07 §7.5 dispute).</summary>
public sealed record DisputeSummaryDto(
    Guid Id,
    string Type,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AutoCheckResult,
    bool CanSubmitTxHash,
    bool CanEscalate,
    DateTime CreatedAt);

/// <summary>Invite info block — surfaced to the seller before the buyer registers.</summary>
public sealed record InviteInfoDto(string InviteUrl, bool BuyerRegistered, bool BuyerNotified);

/// <summary>One element of the <c>paymentEvents</c> array (07 §7.5).</summary>
public sealed record PaymentEventDto(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReceivedAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpectedAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RefundTxHash,
    DateTime OccurredAt);

/// <summary>
/// Available actions block (07 §7.5). Public callers receive only the
/// <c>CanAccept</c> and <c>RequiresLogin</c> flags; authenticated callers
/// receive the full action surface.
/// </summary>
public sealed record AvailableActionsDto(
    bool CanAccept,
    // v3.0 — seller readiness confirmation (03 §2.3) and buyer receipt
    // confirmation (03 §3.5). Omitted for public / prospective-buyer envelopes.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CanConfirmReady,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CanConfirmReceipt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CanCancel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CanDispute,
    // WP5 (T58-canDisputeEnvelopeBit) — per-type dispute eligibility so the FE
    // surfaces only the dispute types currently openable (07 §7.5). Omitted for
    // public / prospective-buyer / on-hold envelopes.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<DisputeType>? DisputableTypes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CanEscalate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? RequiresLogin);

// ---------- Outcome record (controller maps to ActionResult) ----------

public sealed record TransactionDetailOutcome(
    TransactionDetailStatus Status,
    TransactionDetailDto? Body,
    string? ErrorCode,
    string? ErrorMessage);

public enum TransactionDetailStatus
{
    Found,
    NotFound,
    NotAParty,
}
