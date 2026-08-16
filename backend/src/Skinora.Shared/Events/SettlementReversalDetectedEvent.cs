using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// T129 — the settlement re-check concluded the trade was reversed (02 §4.5.1):
/// the item left the buyer's inventory and the seller's original asset is back
/// with them. The transaction has already moved to <c>REFUNDED</c> and a
/// <see cref="PaymentRefundToBuyerRequestedEvent"/> carries the money side; this
/// event carries the telling — buyer, seller and admins.
/// </summary>
/// <remarks>
/// Only raised when the launch gate
/// (<c>settlement.reversal_auto_refund_enabled</c>) is open. While it is closed
/// the same signature raises <see cref="SettlementReviewRequiredEvent"/> instead
/// — nobody is told a reversal happened until a human has agreed that it did.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction that was refunded.</param>
/// <param name="SellerId">Seller the reversal is attributed to.</param>
/// <param name="BuyerId">Buyer receiving the refund.</param>
/// <param name="ItemName">Item snapshot name, for the notification body.</param>
/// <param name="OccurredAt">UTC timestamp the conclusion was committed.</param>
public record SettlementReversalDetectedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid SellerId,
    Guid BuyerId,
    string ItemName,
    DateTime OccurredAt) : IDomainEvent;
