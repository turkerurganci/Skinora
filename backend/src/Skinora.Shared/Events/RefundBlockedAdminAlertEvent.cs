using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by <c>RefundDecisionService</c> (T53 — 02 §4.6, §4.7, 09 §14.4)
/// when a buyer refund or overpayment refund is suppressed because the net
/// payable amount would fall below the minimum threshold
/// (<c>gasFee × min_refund_threshold_ratio</c>).
/// </summary>
/// <remarks>
/// 09 §14.4 is explicit: "iade &lt; minimum: iade yapılmaz → admin alert".
/// The refund flow itself is forward-deferred (T57+ refund orchestrator,
/// T73 blockchain sidecar consumer); T53 ships the alert hook so the
/// suppression is observable from day one. Notification fan-out (admin
/// inbox, email, Telegram) is wired up by T78–T80 consumers — analogous
/// to <see cref="PaymentRefundToBuyerRequestedEvent"/>.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction whose refund was blocked.</param>
/// <param name="Reason">Why the refund was blocked (sub-threshold, negative, …).</param>
/// <param name="TotalPaid">Source amount the refund was being computed from (full <c>TotalAmount</c> for cancellation, overpayment delta otherwise).</param>
/// <param name="GasFee">Gas fee that was about to be deducted.</param>
/// <param name="NetRefund">Computed net refund (<c>TotalPaid − GasFee</c>) — may be negative for diagnostics.</param>
/// <param name="MinimumThreshold">Threshold the net refund failed to clear (<c>GasFee × min_refund_threshold_ratio</c>).</param>
/// <param name="OccurredAt">UTC timestamp the alert was committed.</param>
public record RefundBlockedAdminAlertEvent(
    Guid EventId,
    Guid TransactionId,
    RefundBlockedReason Reason,
    decimal TotalPaid,
    decimal GasFee,
    decimal NetRefund,
    decimal MinimumThreshold,
    DateTime OccurredAt) : IDomainEvent;

/// <summary>
/// Why a refund was blocked. Persisted as a string in audit logs and on
/// the outbox event payload.
/// </summary>
public enum RefundBlockedReason
{
    /// <summary>Net refund (<c>TotalPaid − GasFee</c>) is below
    /// <c>GasFee × min_refund_threshold_ratio</c> (09 §14.4).</summary>
    BelowMinimumThreshold,

    /// <summary>Net refund is negative — gas fee exceeds the source
    /// amount entirely. Treated as a stricter sub-threshold case.</summary>
    NegativeAmount,
}
