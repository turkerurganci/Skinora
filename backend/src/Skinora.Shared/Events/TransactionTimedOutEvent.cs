using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by <c>TimeoutExecutor</c> / <c>DeadlineScannerJob</c> after a
/// successful <c>Timeout</c> trigger fires (T49 — 02 §3.2, 03 §4.1–§4.4). The
/// Notifications consumer fans out a per-party <c>TRANSACTION_CANCELLED</c>
/// notification with phase- and role-specific reason text. Refund and
/// late-payment-monitor side effects are emitted as separate events
/// (<see cref="ItemRefundToSellerRequestedEvent"/>,
/// <see cref="PaymentRefundToBuyerRequestedEvent"/>,
/// <see cref="LatePaymentMonitorRequestedEvent"/>).
/// </summary>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction the timeout applies to.</param>
/// <param name="Phase">Which lifecycle deadline elapsed (03 §4.1–§4.4).</param>
/// <param name="SellerId">Seller user id (always present — sellers must be registered).</param>
/// <param name="BuyerId">Buyer user id, or <c>null</c> if the transaction was open-link/invite-only and the buyer never registered (06 §3.5).</param>
/// <param name="ItemName">Snapshot of the item label, used by templates.</param>
/// <param name="OccurredAt">UTC timestamp the timeout was committed.</param>
public record TransactionTimedOutEvent(
    Guid EventId,
    Guid TransactionId,
    TimeoutPhase Phase,
    Guid SellerId,
    Guid? BuyerId,
    string ItemName,
    DateTime OccurredAt) : IDomainEvent;
