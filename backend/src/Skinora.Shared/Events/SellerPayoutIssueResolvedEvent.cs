using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted when a SellerPayoutIssue reaches the terminal RESOLVED state
/// (07 §7.11, 03 §2.4a Senaryo A). Two trigger paths: (a) automatic
/// blockchain verification confirms the payout was delivered, in which case
/// <see cref="PayoutTxHash"/> carries the verified hash; (b) admin manual
/// resolution after escalation (T63 forward-deferred), in which case
/// <see cref="ResolvedByAdminId"/> is non-null.
/// </summary>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="IssueId">SellerPayoutIssue row id.</param>
/// <param name="TransactionId">Transaction the issue is attached to.</param>
/// <param name="SellerId">Seller user id.</param>
/// <param name="PayoutTxHash">Verified blockchain tx hash (path a) or null (path b).</param>
/// <param name="ResolvedByAdminId">Admin who resolved (path b) or null (path a).</param>
/// <param name="OccurredAt">UTC timestamp the resolution was committed.</param>
public record SellerPayoutIssueResolvedEvent(
    Guid EventId,
    Guid IssueId,
    Guid TransactionId,
    Guid SellerId,
    string? PayoutTxHash,
    Guid? ResolvedByAdminId,
    DateTime OccurredAt) : IDomainEvent;
