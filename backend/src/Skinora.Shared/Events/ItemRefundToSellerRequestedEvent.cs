using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the timeout pipeline (T49 — 02 §3.2, 03 §4.3 / §4.4) when the
/// item is on the platform and must be returned to the seller — i.e. when the
/// transaction was in <c>ITEM_ESCROWED</c> (payment timeout) or
/// <c>TRADE_OFFER_SENT_TO_BUYER</c> (delivery timeout) immediately before the
/// state machine flipped to <c>CANCELLED_TIMEOUT</c>.
/// </summary>
/// <remarks>
/// The Steam sidecar consumer (T64–T68) acts on this event to issue a Steam
/// trade offer that returns the item to the seller. T49 ships a stub consumer
/// that records the request; the production handler is wired up when the
/// sidecar bot session services arrive in F4.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction the refund applies to.</param>
/// <param name="SellerId">Seller user id receiving the item back.</param>
/// <param name="Trigger">Which timeout phase produced the request (Payment or Delivery, 03 §4.3 / §4.4).</param>
/// <param name="OccurredAt">UTC timestamp the request was committed.</param>
public record ItemRefundToSellerRequestedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid SellerId,
    TimeoutPhase Trigger,
    DateTime OccurredAt) : IDomainEvent;
