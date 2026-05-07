using Skinora.Shared.Enums;

namespace Skinora.Realtime.Application.Contracts;

/// <summary>
/// Server→client payloads pushed on <c>/hubs/transactions</c> per 07 §11.1.
/// All payloads are camel-cased on the wire by the SignalR JSON protocol.
/// </summary>
public static class TransactionRealtimePayloads
{
    public sealed record TransactionStatusChanged(
        Guid TransactionId,
        TransactionStatus FromStatus,
        TransactionStatus ToStatus,
        DateTime Timestamp);

    public sealed record CountdownSync(
        Guid TransactionId,
        TimeoutPhase TimeoutType,
        int RemainingSeconds,
        bool Frozen,
        TimeoutFreezeReason? FrozenReason);

    public sealed record PaymentDetected(
        Guid TransactionId,
        decimal Amount,
        string TxHash,
        string Status);

    public sealed record PaymentConfirmed(
        Guid TransactionId,
        decimal Amount,
        string TxHash,
        int Confirmations);

    public sealed record DisputeUpdate(
        Guid TransactionId,
        Guid DisputeId,
        DisputeStatus Status,
        string? AutoCheckResult);

    public sealed record FlagResolved(
        Guid TransactionId,
        ReviewStatus ReviewStatus);

    public sealed record EmergencyHoldApplied(
        Guid TransactionId,
        string Message);

    public sealed record EmergencyHoldReleased(
        Guid TransactionId,
        EmergencyHoldReleaseAction Action,
        TransactionStatus ResumedStatus);
}
