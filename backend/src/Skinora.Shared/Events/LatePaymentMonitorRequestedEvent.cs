using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the timeout pipeline (T49 — 02 §3.2, 03 §4.3) after a payment
/// timeout — i.e. when the transaction was in <c>ITEM_ESCROWED</c> immediately
/// before the state machine flipped to <c>CANCELLED_TIMEOUT</c>. Asks the
/// Blockchain sidecar (T75) to keep watching the platform payment address so
/// any late incoming transfer is auto-refunded to the buyer's refund address.
/// </summary>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction whose payment address is to be monitored.</param>
/// <param name="BuyerId">Buyer user id (refund target).</param>
/// <param name="BuyerRefundAddress">Tron wallet address the buyer set during acceptance (06 §3.5).</param>
/// <param name="OccurredAt">UTC timestamp the request was committed.</param>
public record LatePaymentMonitorRequestedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid BuyerId,
    string BuyerRefundAddress,
    DateTime OccurredAt) : IDomainEvent;
