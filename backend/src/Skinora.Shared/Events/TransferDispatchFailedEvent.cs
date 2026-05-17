using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by <c>OutgoingTransferDispatchJob</c> (T73) when a
/// <c>BlockchainTransaction</c> row has exhausted all configured retry
/// intervals (default 1m → 5m → 15m) and the sidecar still returns a
/// transient failure. The row is flipped to <c>Status=FAILED</c>; this event
/// pushes an admin alert through the notification pipeline (08 §3.3 "tüm
/// denemeler başarısızsa admin'e critical alert").
/// </summary>
/// <remarks>
/// User-facing notifications (buyer/seller) are intentionally not emitted —
/// transfer failure is an operator concern. The buyer's refund row stays in
/// FAILED state until an admin manually retries (T96 admin endpoint forward
/// devir) or escalates to manual hot-wallet transfer.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="BlockchainTransactionId">Row that exhausted retries.</param>
/// <param name="TransactionId">Parent transaction for cross-reference.</param>
/// <param name="Type">SELLER_PAYOUT / BUYER_REFUND / EXCESS_REFUND / WRONG_TOKEN_REFUND / INCORRECT_AMOUNT_REFUND / LATE_PAYMENT_REFUND.</param>
/// <param name="Token">USDT or USDC.</param>
/// <param name="Amount">Net amount that failed to dispatch.</param>
/// <param name="ToAddress">Intended destination (seller or buyer source address).</param>
/// <param name="LastErrorCode">Sidecar error code from the final attempt (e.g. TRANSFER_BROADCAST_REJECTED).</param>
/// <param name="LastErrorMessage">Sidecar error message from the final attempt.</param>
/// <param name="RetryCount">Number of attempts performed (matches the row's RetryCount column).</param>
/// <param name="OccurredAt">UTC timestamp the event was committed.</param>
public record TransferDispatchFailedEvent(
    Guid EventId,
    Guid BlockchainTransactionId,
    Guid TransactionId,
    BlockchainTransactionType Type,
    StablecoinType Token,
    decimal Amount,
    string ToAddress,
    string? LastErrorCode,
    string? LastErrorMessage,
    int RetryCount,
    DateTime OccurredAt) : IDomainEvent;
