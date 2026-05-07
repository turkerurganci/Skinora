using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Admin;

// ---------- AD6 — GET /admin/transactions (07 §9.6) ----------

/// <summary>One row of the AD6 list (07 §9.6).</summary>
public sealed record AdminTransactionListItemDto(
    Guid Id,
    string ItemName,
    string? ItemImageUrl,
    decimal Price,
    StablecoinType Stablecoin,
    TransactionStatus Status,
    AdminTransactionPartyDto Seller,
    AdminTransactionPartyDto? Buyer,
    DateTime CreatedAt,
    DateTime? CompletedAt);

/// <summary>Buyer/seller view used by AD6 + AD7 (07 §9.6 / §9.7).</summary>
public sealed record AdminTransactionPartyDto(
    string SteamId,
    string DisplayName,
    string? AvatarUrl);

// ---------- AD7 — GET /admin/transactions/:id (07 §9.7) ----------

/// <summary>
/// Full admin transaction detail. Mirrors the user-facing T5 detail (price,
/// timeouts, status) and adds the eight admin-only sections enumerated in
/// 07 §9.7. Sections backed by tables that are not yet wired (e.g. some
/// notification fan-outs land with T78–T80) come back as empty arrays so
/// the contract shape is stable from day one.
/// </summary>
public sealed record AdminTransactionDetailDto(
    Guid Id,
    TransactionStatus Status,
    string ItemName,
    string? ItemImageUrl,
    string? ItemExterior,
    string? ItemInspectLink,
    decimal Price,
    StablecoinType Stablecoin,
    decimal CommissionRate,
    decimal CommissionAmount,
    decimal TotalAmount,
    int PaymentTimeoutMinutes,
    AdminTransactionPartyDto Seller,
    AdminTransactionPartyDto? Buyer,
    DateTime CreatedAt,
    DateTime? AcceptedAt,
    DateTime? ItemEscrowedAt,
    DateTime? PaymentReceivedAt,
    DateTime? ItemDeliveredAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    string? CancelReason,
    bool IsOnHold,
    DateTime? EmergencyHoldAt,
    string? EmergencyHoldReason,
    IReadOnlyList<AdminTxStatusHistoryDto> StatusHistory,
    AdminTxPaymentDetailDto? PaymentDetail,
    AdminTxSellerPayoutDetailDto? SellerPayoutDetail,
    AdminTxRefundDetailDto? RefundDetail,
    IReadOnlyList<AdminTxNotificationDto> NotificationHistory,
    IReadOnlyList<AdminTxDisputeDto> DisputeHistory,
    IReadOnlyList<AdminTxFlagDto> FlagHistory,
    AdminTxAdminActionsDto AdminActions);

/// <summary>One row of <c>statusHistory</c> (07 §9.7).</summary>
public sealed record AdminTxStatusHistoryDto(
    TransactionStatus? FromStatus,
    TransactionStatus ToStatus,
    DateTime ChangedAt,
    string Trigger);

/// <summary><c>paymentDetail</c> — null until the buyer-payment row exists (07 §9.7).</summary>
public sealed record AdminTxPaymentDetailDto(
    string? PaymentAddress,
    decimal ReceivedAmount,
    string? ReceivedTxHash,
    int BlockConfirmations,
    DateTime? ConfirmedAt);

/// <summary>
/// <c>sellerPayoutDetail</c> — null until the SELLER_PAYOUT row exists.
/// The two gas-fee splits (<c>gasFeeFromCommission</c> / <c>gasFeeFromSeller</c>)
/// are forward-deferred to T57 / T73 (gas fee management + Tron sidecar);
/// implementations may emit zero placeholders today.
/// </summary>
public sealed record AdminTxSellerPayoutDetailDto(
    decimal GrossAmount,
    decimal Commission,
    decimal? GasFee,
    decimal GasFeeFromCommission,
    decimal GasFeeFromSeller,
    decimal NetAmount,
    string? TxHash,
    DateTime? SentAt);

/// <summary>
/// <c>refundDetail</c> — null until any *_REFUND row exists.
/// Aggregates across BUYER_REFUND / EXCESS_REFUND / WRONG_TOKEN_REFUND /
/// LATE_PAYMENT_REFUND / INCORRECT_AMOUNT_REFUND (06 §3.8 enum).
/// </summary>
public sealed record AdminTxRefundDetailDto(
    decimal OriginalAmount,
    decimal? GasFee,
    decimal NetRefundAmount,
    string? RefundAddress,
    string? TxHash,
    DateTime? RefundedAt);

/// <summary>One row of <c>notificationHistory</c> (07 §9.7).</summary>
public sealed record AdminTxNotificationDto(
    string Type,
    string Recipient,
    IReadOnlyList<string> Channels,
    DateTime SentAt);

/// <summary>One row of <c>disputeHistory</c> (07 §9.7).</summary>
public sealed record AdminTxDisputeDto(
    Guid Id,
    string Type,
    string Status,
    string? AutoCheckResult,
    DateTime EscalatedAt,
    DateTime? ClosedAt);

/// <summary>One row of <c>flagHistory</c> (07 §9.7).</summary>
public sealed record AdminTxFlagDto(
    Guid Id,
    string Type,
    string ReviewStatus,
    string? AdminNote,
    DateTime? ReviewedAt);

/// <summary><c>adminActions</c> — derived from current state (07 §9.7).</summary>
public sealed record AdminTxAdminActionsDto(
    bool CanApproveFlag,
    bool CanRejectFlag,
    bool CanCancel);
