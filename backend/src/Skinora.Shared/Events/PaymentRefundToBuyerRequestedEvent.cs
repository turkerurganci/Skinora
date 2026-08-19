using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the timeout pipeline (T49 — 02 §3.2, 03 §4.4) when the payment
/// is already held by the platform and must be refunded to the buyer — i.e. when
/// the transaction leaves <c>PAYMENT_RECEIVED</c> without a completed delivery
/// (delivery timeout per 02 §3.2 / 03 §4.4, admin cancel, buyer-favour dispute
/// resolution, or a detected trade reversal at settlement). There is no item
/// leg: the platform never held the item, so a refund moves money only.
/// </summary>
/// <remarks>
/// The Blockchain sidecar consumer (T73) acts on this event to send the
/// stablecoin refund to <see cref="BuyerRefundAddress"/>. The refund amount
/// equals price + commission − gas fee (02 §4.6). T49 ships a stub consumer
/// that records the request; the production handler is wired up when the
/// transfer service arrives in F4.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction the refund applies to.</param>
/// <param name="BuyerId">Buyer user id receiving the payment refund.</param>
/// <param name="BuyerRefundAddress">Tron wallet address the buyer set during acceptance (06 §3.5).</param>
/// <param name="OccurredAt">UTC timestamp the request was committed.</param>
public record PaymentRefundToBuyerRequestedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid BuyerId,
    string BuyerRefundAddress,
    DateTime OccurredAt) : IDomainEvent;
