using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the T58 dispute pipeline when a dispute is escalated to admin
/// review. Two trigger paths:
/// (a) <c>POST /transactions/:id/disputes/:disputeId/escalate</c> — the buyer
/// invokes the user-facing escalation endpoint (03 §6.4); and
/// (b) automatic escalation by the WRONG_ITEM auto-checker when the on-platform
/// item snapshot diverges from the delivered item (03 §6.3, 02 §10.1) — both
/// parties are then notified that the transaction has been put under review.
/// </summary>
/// <remarks>
/// <para>
/// The Notifications consumer fans out per <see cref="AutoEscalated"/>:
/// <list type="bullet">
///   <item>
///     <c>AutoEscalated=false</c> (manual) — single
///     <see cref="NotificationType.DISPUTE_RESULT"/> to the buyer with
///     <c>"İtirazınız admin ekibine iletildi"</c>. Admin queue surfacing is
///     T63's responsibility (admin dashboard).
///   </item>
///   <item>
///     <c>AutoEscalated=true</c> (WRONG_ITEM auto) — two
///     <see cref="NotificationType.DISPUTE_RESULT"/> sends, one per party,
///     with <c>"İşleminiz incelemeye alındı"</c> per 03 §6.3 step 5.
///   </item>
/// </list>
/// </para>
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="DisputeId">Dispute row that just transitioned to ESCALATED.</param>
/// <param name="TransactionId">Transaction the dispute is attached to.</param>
/// <param name="Type">Dispute type (PAYMENT / DELIVERY / WRONG_ITEM).</param>
/// <param name="SellerId">Seller user id — used by the WRONG_ITEM auto-escalation fan-out.</param>
/// <param name="BuyerId">Buyer user id (always present — disputes are buyer-initiated).</param>
/// <param name="AutoEscalated"><c>true</c> when the WRONG_ITEM auto-checker fired the escalation; <c>false</c> for buyer-initiated.</param>
/// <param name="Detail">Buyer-supplied detail (≥10 chars trimmed, only when <see cref="AutoEscalated"/> is false).</param>
/// <param name="OccurredAt">UTC timestamp the escalation was committed.</param>
/// <param name="OutcomeText">
/// WP17 — pre-localized outcome fragment for the DISPUTE_RESULT notification
/// <c>{Outcome}</c> parameter. Set by the manual-escalate path (single buyer
/// recipient) in the buyer's locale; <c>null</c> for the auto-escalated
/// two-party path (each recipient needs its own locale — deferred), where the
/// consumer keeps its hardcoded fallback.
/// </param>
public record DisputeEscalatedEvent(
    Guid EventId,
    Guid DisputeId,
    Guid TransactionId,
    DisputeType Type,
    Guid SellerId,
    Guid BuyerId,
    bool AutoEscalated,
    string? Detail,
    DateTime OccurredAt,
    string? OutcomeText = null) : IDomainEvent;
