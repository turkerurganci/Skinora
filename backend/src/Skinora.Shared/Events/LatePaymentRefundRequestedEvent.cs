using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by T75 when the blockchain sidecar detects a buyer transfer at a
/// deposit address whose transaction is already in a terminal cancel state
/// (<c>CANCELLED_TIMEOUT</c> / <c>CANCELLED_SELLER</c> / <c>CANCELLED_BUYER</c>
/// / <c>CANCELLED_ADMIN</c>) and the platform is still inside the 30-day
/// post-cancel monitoring window (06 §2.16 / 08 §3.4 gecikmeli ödeme).
/// </summary>
/// <remarks>
/// The handler that publishes this event also writes a
/// <c>LATE_PAYMENT_REFUND</c> BlockchainTransaction row at
/// <c>Status=PENDING</c>; the existing T73 dispatch pipeline broadcasts the
/// refund. Sub-threshold transfers (received &lt; 2× gas) emit
/// <see cref="RefundBlockedAdminAlertEvent"/> instead.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">The originally-cancelled transaction.</param>
/// <param name="BuyerId">Buyer user id (notification recipient).</param>
/// <param name="RefundTransactionId">
/// Identifier of the <c>LATE_PAYMENT_REFUND</c> BlockchainTransaction row
/// queued for T73 dispatch.
/// </param>
/// <param name="ReceivedAmount">Confirmed on-chain amount that landed late.</param>
/// <param name="Stablecoin">USDT or USDC — the expected token.</param>
/// <param name="SourceAddress">Refund destination — parsed from the inbound transfer.</param>
/// <param name="TxHash">Inbound transaction hash for cross-reference.</param>
/// <param name="MonitorState">Post-cancel state observed at detection
/// (audit only — drives the operator dashboard window bucket).</param>
/// <param name="OccurredAt">UTC timestamp the event was committed.</param>
public record LatePaymentRefundRequestedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid BuyerId,
    Guid RefundTransactionId,
    decimal ReceivedAmount,
    StablecoinType Stablecoin,
    string SourceAddress,
    string TxHash,
    MonitoringStatus MonitorState,
    DateTime OccurredAt) : IDomainEvent;
