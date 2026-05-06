using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted when a SellerPayoutIssue transitions to ESCALATED — automatic
/// verification could not resolve the report and admin attention is required
/// (07 §7.11, 03 §2.4a Senaryo A son adım, 02 §10.3 "Eskalasyon").
/// </summary>
/// <remarks>
/// The Notifications consumer fans out an
/// <see cref="Skinora.Shared.Enums.NotificationType.ADMIN_PAYMENT_FAILURE"/>
/// to the assigned admin (T63 admin queue surfacing). The
/// <see cref="EscalatedToAdminId"/> is the admin row that owns the issue
/// from this point on; the seller is notified separately by the response
/// payload + the transaction detail screen.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="IssueId">SellerPayoutIssue row id.</param>
/// <param name="TransactionId">Transaction the issue is attached to.</param>
/// <param name="SellerId">Seller user id.</param>
/// <param name="EscalatedToAdminId">Admin user id who owns the escalation.</param>
/// <param name="VerificationMessage">Verifier-supplied reason for escalation.</param>
/// <param name="OccurredAt">UTC timestamp the escalation was committed.</param>
public record SellerPayoutIssueEscalatedEvent(
    Guid EventId,
    Guid IssueId,
    Guid TransactionId,
    Guid SellerId,
    Guid EscalatedToAdminId,
    string VerificationMessage,
    DateTime OccurredAt) : IDomainEvent;
