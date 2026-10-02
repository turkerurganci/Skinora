using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// P2P-DeliveryPollingJob (owner decision 2026-10-02, 02 §9.2) — the delivery
/// poll observed, in one round, the seller's asset leave their inventory AND
/// the buyer's count of that item class rise, while the inventory-evidence
/// launch gate (DEPLOY_RUNBOOK §H) is closed. Money does not move on that
/// inference; the Notifications consumer asks the buyer to check their Steam
/// inventory and confirm receipt themselves (<c>DELIVERY_DETECTED</c>).
/// </summary>
/// <remarks>
/// Published at most once per transaction: it rides the same unit of work as
/// the evidence flags that make the transaction's recorded evidence
/// sufficient, and the poll never selects a transaction whose evidence is
/// already sufficient.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction whose delivery was observed.</param>
/// <param name="BuyerId">The buyer — the notification's only recipient.</param>
/// <param name="ItemName">Snapshot of the item label, used by templates.</param>
/// <param name="OccurredAt">UTC time of the observation.</param>
public record DeliveryDetectedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid BuyerId,
    string ItemName,
    DateTime OccurredAt) : IDomainEvent;
