using Skinora.Realtime.Application.Contracts;

namespace Skinora.Realtime.Application;

/// <summary>
/// Publishes server→client events on the <c>/hubs/transactions</c> channel
/// (T61 — 07 §11.1 RT1). Implementations target the per-transaction group
/// <c>tx:{transactionId:N}</c>; both buyer and seller receive the push while
/// connected to the detail page (S07).
/// </summary>
/// <remarks>
/// All methods are best-effort fire-and-forget at the application boundary:
/// failures (no subscribers, transport errors) must not propagate as
/// exceptions to the calling event consumer because the outbox dispatcher
/// would interpret an exception as a redelivery signal. Concrete adapters
/// log and swallow.
/// </remarks>
public interface ITransactionRealtimePublisher
{
    Task PublishStatusChangedAsync(
        TransactionRealtimePayloads.TransactionStatusChanged payload,
        CancellationToken cancellationToken);

    Task PublishCountdownSyncAsync(
        TransactionRealtimePayloads.CountdownSync payload,
        CancellationToken cancellationToken);

    Task PublishPaymentDetectedAsync(
        TransactionRealtimePayloads.PaymentDetected payload,
        CancellationToken cancellationToken);

    Task PublishPaymentConfirmedAsync(
        TransactionRealtimePayloads.PaymentConfirmed payload,
        CancellationToken cancellationToken);

    Task PublishDisputeUpdateAsync(
        TransactionRealtimePayloads.DisputeUpdate payload,
        CancellationToken cancellationToken);

    Task PublishFlagResolvedAsync(
        TransactionRealtimePayloads.FlagResolved payload,
        CancellationToken cancellationToken);

    Task PublishEmergencyHoldAppliedAsync(
        TransactionRealtimePayloads.EmergencyHoldApplied payload,
        CancellationToken cancellationToken);

    Task PublishEmergencyHoldReleasedAsync(
        TransactionRealtimePayloads.EmergencyHoldReleased payload,
        CancellationToken cancellationToken);
}
