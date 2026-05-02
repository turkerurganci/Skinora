using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted when the buyer accepts a transaction (T46 — 07 §7.6, 03 §3.2).
/// Drives the seller-side <c>BUYER_ACCEPTED</c> notification fan-out
/// (07 §8.1) and downstream side effects scheduled by T47/T62/T78–T80
/// consumers; the producer publishes atomically with the
/// <c>CREATED → ACCEPTED</c> state transition.
/// </summary>
public record BuyerAcceptedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid SellerId,
    Guid BuyerId,
    string ItemName,
    DateTime AcceptedAt,
    DateTime OccurredAt) : IDomainEvent;
