using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by <c>TransactionCancellationService</c> after a successful
/// user-initiated cancel transition (T51 — 02 §7, 03 §2.5 / §3.3, 07 §7.7).
/// The Notifications consumer fans out a <c>TRANSACTION_CANCELLED</c>
/// notification to the counter-party with role-aware Turkish reason text.
/// </summary>
/// <remarks>
/// Item-return and (future) payment-refund side effects are emitted as separate
/// events (<see cref="ItemRefundToSellerRequestedEvent"/>) so the Steam sidecar
/// pipeline can reuse a single handler regardless of cancellation origin.
/// Admin-initiated cancellation (T59) and timeout-initiated cancellation
/// (T49 — see <see cref="TransactionTimedOutEvent"/>) emit their own dedicated
/// events because the counter-party reason text differs significantly.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction the cancellation applies to.</param>
/// <param name="CancelledBy">Which party initiated the cancellation (SELLER or BUYER).</param>
/// <param name="SellerId">Seller user id (always present — sellers must be registered).</param>
/// <param name="BuyerId">Buyer user id, or <c>null</c> when no buyer had accepted yet (06 §3.5 — pre-accept seller cancel).</param>
/// <param name="ItemName">Snapshot of the item label, used by templates.</param>
/// <param name="CancelReason">Free-text reason supplied by the cancelling party (≥10 chars after trim, 02 §7 / 07 §7.7).</param>
/// <param name="OccurredAt">UTC timestamp the cancellation was committed.</param>
public record TransactionCancelledEvent(
    Guid EventId,
    Guid TransactionId,
    CancelledByType CancelledBy,
    Guid SellerId,
    Guid? BuyerId,
    string ItemName,
    string CancelReason,
    DateTime OccurredAt) : IDomainEvent;
