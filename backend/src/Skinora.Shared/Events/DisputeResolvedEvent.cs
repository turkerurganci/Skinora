using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted when an admin resolves an ESCALATED dispute (WP5 / T58 — admin
/// dispute resolution, 02 §10.4 / 03 §6.4). Both parties are notified of the
/// outcome via <see cref="NotificationType.DISPUTE_RESULT"/>.
/// </summary>
/// <remarks>
/// The transaction-side effects (REFUNDED transition, buyer payment refund,
/// seller item-return) are published as separate events by the resolve service
/// inside the same unit of work; this event is purely the user-facing
/// notification signal so the buyer and seller learn the decision.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="DisputeId">Dispute row that transitioned to RESOLVED_FOR_*.</param>
/// <param name="TransactionId">Transaction the dispute is attached to.</param>
/// <param name="Type">Dispute type (PAYMENT / DELIVERY / WRONG_ITEM).</param>
/// <param name="SellerId">Seller user id.</param>
/// <param name="BuyerId">Buyer user id (the dispute opener).</param>
/// <param name="Outcome">Which party the admin resolved in favor of.</param>
/// <param name="BuyerRefunded"><c>true</c> when a buyer payment refund was queued (BUYER_FAVOR with a paid transaction).</param>
/// <param name="OccurredAt">UTC timestamp the resolution was committed.</param>
public record DisputeResolvedEvent(
    Guid EventId,
    Guid DisputeId,
    Guid TransactionId,
    DisputeType Type,
    Guid SellerId,
    Guid BuyerId,
    DisputeResolutionOutcome Outcome,
    bool BuyerRefunded,
    DateTime OccurredAt) : IDomainEvent;
