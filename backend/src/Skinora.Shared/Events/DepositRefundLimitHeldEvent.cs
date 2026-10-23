using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// T169a — emitted by <c>OutgoingTransferDispatchJob</c> the first time its
/// network's deposit refund daily limit holds a refund back (08 §3.3c,
/// <c>blockchain.*.deposit_refund_daily_limit_usdt</c>). The row stays PENDING
/// (no retry spent, never FAILED) and goes out when the 24-hour window has
/// room again or an admin releases it (AD42); this event pushes the admin
/// alert through the notification pipeline. Raised once per row — the held
/// timestamp is written with it.
/// </summary>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="BlockchainTransactionId">The held refund row.</param>
/// <param name="TransactionId">Parent transaction for cross-reference.</param>
/// <param name="Network">The network whose limit holds it.</param>
/// <param name="Type">The refund type.</param>
/// <param name="Amount">What the refund would send.</param>
/// <param name="UsedLast24hUsdt">What the network's deposit refunds sent in the last 24 hours.</param>
/// <param name="LimitUsdt">The network's limit.</param>
/// <param name="OccurredAt">UTC timestamp the hold was committed.</param>
public record DepositRefundLimitHeldEvent(
    Guid EventId,
    Guid BlockchainTransactionId,
    Guid TransactionId,
    BlockchainNetwork Network,
    BlockchainTransactionType Type,
    decimal Amount,
    decimal UsedLast24hUsdt,
    decimal LimitUsdt,
    DateTime OccurredAt) : IDomainEvent;
