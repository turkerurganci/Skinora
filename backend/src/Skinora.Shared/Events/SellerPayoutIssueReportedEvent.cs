using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the T60 payout-issue pipeline when the seller reports a payout
/// problem on a COMPLETED transaction (07 §7.11, 03 §2.4a Senaryo A).
/// </summary>
/// <remarks>
/// Notification fan-out is intentionally deferred — the seller already sees
/// the report acknowledgement in the synchronous response payload, and admin
/// surfacing is owned by <see cref="SellerPayoutIssueEscalatedEvent"/> when
/// the verification pipeline cannot resolve the issue automatically.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="IssueId">SellerPayoutIssue row id.</param>
/// <param name="TransactionId">Transaction the issue is attached to.</param>
/// <param name="SellerId">Seller user id.</param>
/// <param name="OccurredAt">UTC timestamp the report was committed.</param>
public record SellerPayoutIssueReportedEvent(
    Guid EventId,
    Guid IssueId,
    Guid TransactionId,
    Guid SellerId,
    DateTime OccurredAt) : IDomainEvent;
