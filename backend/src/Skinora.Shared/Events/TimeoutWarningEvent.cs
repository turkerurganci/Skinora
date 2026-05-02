using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted when the per-transaction Hangfire warning job fires for a payment
/// timeout (T48 — 02 §3.4, 05 §4.4). The Notifications consumer translates it
/// into a <c>TIMEOUT_WARNING</c> fan-out targeting the buyer (the only party
/// awaiting an action during ITEM_ESCROWED).
/// </summary>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction the warning applies to.</param>
/// <param name="RecipientUserId">Party the warning is addressed to (buyer for the payment stage).</param>
/// <param name="ItemName">Snapshot of the item label, used by templates.</param>
/// <param name="RemainingMinutes">Floored minutes left until the payment deadline at dispatch time.</param>
/// <param name="OccurredAt">UTC timestamp the warning was stamped.</param>
public record TimeoutWarningEvent(
    Guid EventId,
    Guid TransactionId,
    Guid RecipientUserId,
    string ItemName,
    int RemainingMinutes,
    DateTime OccurredAt) : IDomainEvent;
