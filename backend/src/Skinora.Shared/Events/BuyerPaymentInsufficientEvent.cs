using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the T72 amount validation pipeline when a confirmed buyer
/// payment carries the expected token but the amount is strictly less than
/// the snapshot in <c>PaymentAddress.ExpectedAmount</c> (02 §4.4 "Eksik tutar",
/// 08 §3.4 tutar doğrulama tablosu).
/// </summary>
/// <remarks>
/// The state machine stays in <c>SELLER_CONFIRMED</c> (timeout countdown
/// continues) and a <c>BlockchainTransaction</c> row of type
/// <c>INCORRECT_AMOUNT_REFUND</c> is queued at <c>Status=PENDING</c>; T73
/// blockchain sidecar consumer dispatches the actual TRC-20 transfer.
/// Sub-threshold cases (<c>received − gasFee &lt; gasFee × min_refund_threshold_ratio</c>)
/// emit <see cref="RefundBlockedAdminAlertEvent"/> instead and skip the
/// refund row entirely; this event carries the regular-path classification.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction the underpayment applies to.</param>
/// <param name="BuyerId">Buyer user id (notification recipient).</param>
/// <param name="RefundTransactionId">
/// Identifier of the <c>INCORRECT_AMOUNT_REFUND</c> BlockchainTransaction row
/// queued for T73 dispatch. Trace key for refund correlation.
/// </param>
/// <param name="ExpectedAmount">Snapshot amount from PaymentAddress (06 §3.7).</param>
/// <param name="ReceivedAmount">Confirmed on-chain amount.</param>
/// <param name="Stablecoin">USDT or USDC — the expected token.</param>
/// <param name="SourceAddress">
/// Refund destination — parsed from the inbound TRC-20 transfer
/// <c>from</c> field (02 §4.6 source-address rule).
/// </param>
/// <param name="TxHash">Inbound transaction hash for cross-reference.</param>
/// <param name="OccurredAt">UTC timestamp the event was committed.</param>
public record BuyerPaymentInsufficientEvent(
    Guid EventId,
    Guid TransactionId,
    Guid BuyerId,
    Guid RefundTransactionId,
    decimal ExpectedAmount,
    decimal ReceivedAmount,
    StablecoinType Stablecoin,
    string SourceAddress,
    string TxHash,
    DateTime OccurredAt) : IDomainEvent;
