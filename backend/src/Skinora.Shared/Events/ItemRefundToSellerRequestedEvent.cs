using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted whenever the platform-held item must be returned to the seller —
/// the timeout pipeline (T49 — 02 §3.2, 03 §4.3 / §4.4) and the user-cancel
/// pipeline (T51 — 02 §7, 03 §2.5 / §3.3) both publish this event when the
/// transaction was in <c>ITEM_ESCROWED</c> (or a state that has the item on
/// the platform) immediately before the cancelling state machine transition.
/// </summary>
/// <remarks>
/// The Steam sidecar consumer (T64–T68) acts on this event to issue a Steam
/// trade offer that returns the item to the seller. The intermediate stub
/// consumer simply records the request; the production handler is wired up
/// when the sidecar bot session services arrive in F4.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction the refund applies to.</param>
/// <param name="SellerId">Seller user id receiving the item back.</param>
/// <param name="Trigger">Which lifecycle path produced the request.</param>
/// <param name="OccurredAt">UTC timestamp the request was committed.</param>
public record ItemRefundToSellerRequestedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid SellerId,
    ItemRefundTrigger Trigger,
    DateTime OccurredAt) : IDomainEvent;
