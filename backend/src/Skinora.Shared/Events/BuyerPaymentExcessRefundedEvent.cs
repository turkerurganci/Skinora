using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the T72 amount validation pipeline when a confirmed buyer
/// payment carries the expected token but the amount is strictly greater
/// than the snapshot in <c>PaymentAddress.ExpectedAmount</c> (02 §4.4 "Fazla
/// tutar", 08 §3.4 tutar doğrulama tablosu) — or when an extra payment
/// arrives for a transaction that has already advanced past
/// <c>ITEM_ESCROWED</c> (multi-payment case in 02 §4.4).
/// </summary>
/// <remarks>
/// Overpayment: state machine fires <c>ConfirmPayment</c>
/// (<c>ITEM_ESCROWED → PAYMENT_RECEIVED</c>) and the excess
/// <c>received − expected</c> is queued as <c>EXCESS_REFUND</c> at
/// <c>Status=PENDING</c>. Multi-payment: state machine does not advance
/// (already past); the full <c>received</c> amount is queued as
/// <c>EXCESS_REFUND</c>. Sub-threshold cases emit
/// <see cref="RefundBlockedAdminAlertEvent"/> instead.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction the overpayment applies to.</param>
/// <param name="BuyerId">Buyer user id (notification recipient).</param>
/// <param name="RefundTransactionId">
/// Identifier of the <c>EXCESS_REFUND</c> BlockchainTransaction row queued
/// for T73 dispatch.
/// </param>
/// <param name="ExpectedAmount">Snapshot amount from PaymentAddress (06 §3.7).</param>
/// <param name="ReceivedAmount">Confirmed on-chain amount.</param>
/// <param name="ExcessAmount">Refund target (delta for overpayment, full
/// amount for multi-payment).</param>
/// <param name="Stablecoin">USDT or USDC — the expected token.</param>
/// <param name="SourceAddress">Refund destination — parsed from the inbound transfer.</param>
/// <param name="TxHash">Inbound transaction hash for cross-reference.</param>
/// <param name="IsMultiPayment">
/// <c>true</c> when this event represents a stray transfer after
/// <c>ITEM_ESCROWED</c> exited (multi-payment), <c>false</c> for the regular
/// single-shot overpayment.
/// </param>
/// <param name="OccurredAt">UTC timestamp the event was committed.</param>
public record BuyerPaymentExcessRefundedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid BuyerId,
    Guid RefundTransactionId,
    decimal ExpectedAmount,
    decimal ReceivedAmount,
    decimal ExcessAmount,
    StablecoinType Stablecoin,
    string SourceAddress,
    string TxHash,
    bool IsMultiPayment,
    DateTime OccurredAt) : IDomainEvent;
