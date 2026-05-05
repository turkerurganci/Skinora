using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the T59 admin emergency hold orchestrator after the
/// <c>POST /admin/transactions/:id/release-hold</c> endpoint completes a
/// <c>RESUME</c> action (07 §9.22 AD19c). For a <c>CANCEL</c> action the
/// orchestrator emits <see cref="TransactionCancelledEvent"/> (with
/// <c>CancelledBy = ADMIN</c>) instead so the existing cancellation
/// notification path covers both AD19 and AD19c CANCEL.
/// </summary>
/// <remarks>
/// SignalR <c>EmergencyHoldReleased</c> RT1 event (07 §11.1) is forward-deferred
/// to T61 — the same outbox row will feed the SignalR hub consumer.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction the hold was released on.</param>
/// <param name="SellerId">Seller user id (always present).</param>
/// <param name="BuyerId">Buyer user id, or <c>null</c> when no buyer had accepted yet.</param>
/// <param name="ItemName">Snapshot of the item label, used by templates.</param>
/// <param name="Action">Always <see cref="EmergencyHoldReleaseAction.RESUME"/> for this event.</param>
/// <param name="ResumedStatus">Status the transaction resumed to (matches <c>PreviousStatusBeforeHold</c>).</param>
/// <param name="OccurredAt">UTC timestamp the release was committed.</param>
public record EmergencyHoldReleasedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid SellerId,
    Guid? BuyerId,
    string ItemName,
    EmergencyHoldReleaseAction Action,
    TransactionStatus ResumedStatus,
    DateTime OccurredAt) : IDomainEvent;
