using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the T59 admin emergency hold orchestrator after a transaction
/// has been frozen via <c>POST /admin/transactions/:id/emergency-hold</c>
/// (07 §9.21, 02 §7, 03 §8.8). The Notifications consumer fans out an
/// <c>EMERGENCY_HOLD_APPLIED</c> notification to seller + buyer (when buyer
/// is registered).
/// </summary>
/// <remarks>
/// SignalR <c>EmergencyHoldApplied</c> RT1 event (07 §11.1) is forward-deferred
/// to T61 — the same outbox row will feed the SignalR hub consumer.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction the hold was applied to.</param>
/// <param name="SellerId">Seller user id (always present).</param>
/// <param name="BuyerId">Buyer user id, or <c>null</c> when no buyer had accepted yet.</param>
/// <param name="ItemName">Snapshot of the item label, used by templates.</param>
/// <param name="Reason">Free-text hold reason supplied by the admin (≥10 chars, 07 §9.21).</param>
/// <param name="OccurredAt">UTC timestamp the hold was committed.</param>
public record EmergencyHoldAppliedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid SellerId,
    Guid? BuyerId,
    string ItemName,
    string Reason,
    DateTime OccurredAt) : IDomainEvent;
