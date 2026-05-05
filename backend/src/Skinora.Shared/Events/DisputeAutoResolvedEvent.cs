using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the T58 dispute pipeline when an auto-check resolves a dispute
/// without admin involvement (02 §10.1, 03 §6.1 / §6.2). Two trigger paths:
/// (a) <c>POST /transactions/:id/disputes</c> when the on-open auto-check
/// already returns <c>resolved=true</c>, and (b)
/// <c>POST /transactions/:id/disputes/:disputeId/submit-txhash</c> when a
/// retry against the buyer-supplied hash succeeds.
/// </summary>
/// <remarks>
/// The Notifications consumer fans out a single
/// <see cref="NotificationType.DISPUTE_RESULT"/> notification to the buyer
/// using <see cref="Outcome"/> as the template's <c>{Outcome}</c> placeholder.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="DisputeId">Dispute row that just transitioned to CLOSED.</param>
/// <param name="TransactionId">Transaction the dispute is attached to.</param>
/// <param name="Type">Dispute type (PAYMENT / DELIVERY / WRONG_ITEM).</param>
/// <param name="BuyerId">Buyer user id (always present — disputes are buyer-initiated).</param>
/// <param name="Outcome">Free-text resolution message rendered into the buyer notification.</param>
/// <param name="OccurredAt">UTC timestamp the resolution was committed.</param>
public record DisputeAutoResolvedEvent(
    Guid EventId,
    Guid DisputeId,
    Guid TransactionId,
    DisputeType Type,
    Guid BuyerId,
    string Outcome,
    DateTime OccurredAt) : IDomainEvent;
