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
    TransactionStatus Status,
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EscrowBotAssetId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeliveredBuyerAssetId,
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

/// <summary>Payment block; populated from <c>ITEM_ESCROWED</c> onwards.</summary>
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
public sealed record CancelInfoDto(
    string CancelledBy,
    string Reason,
    DateTime CancelledAt,
    bool ItemReturned,
    bool PaymentRefunded);

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CanCancel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CanDispute,
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
