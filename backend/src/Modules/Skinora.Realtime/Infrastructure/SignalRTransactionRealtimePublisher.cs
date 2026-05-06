using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;
using Skinora.Realtime.Hubs;

namespace Skinora.Realtime.Infrastructure;

/// <summary>
/// SignalR-backed implementation of <see cref="ITransactionRealtimePublisher"/>.
/// Resolves <see cref="IHubContext{T}"/> for <see cref="TransactionsHub"/> and
/// pushes payloads to the per-transaction group named by
/// <see cref="TransactionsHub.GroupName(Guid)"/>.
/// </summary>
public sealed class SignalRTransactionRealtimePublisher : ITransactionRealtimePublisher
{
    private const string TransactionStatusChangedEvent = "TransactionStatusChanged";
    private const string CountdownSyncEvent = "CountdownSync";
    private const string PaymentDetectedEvent = "PaymentDetected";
    private const string PaymentConfirmedEvent = "PaymentConfirmed";
    private const string DisputeUpdateEvent = "DisputeUpdate";
    private const string FlagResolvedEvent = "FlagResolved";
    private const string EmergencyHoldAppliedEvent = "EmergencyHoldApplied";
    private const string EmergencyHoldReleasedEvent = "EmergencyHoldReleased";

    private readonly IHubContext<TransactionsHub> _hub;
    private readonly ILogger<SignalRTransactionRealtimePublisher> _logger;

    public SignalRTransactionRealtimePublisher(
        IHubContext<TransactionsHub> hub,
        ILogger<SignalRTransactionRealtimePublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task PublishStatusChangedAsync(
        TransactionRealtimePayloads.TransactionStatusChanged payload,
        CancellationToken cancellationToken) =>
        SendAsync(payload.TransactionId, TransactionStatusChangedEvent, payload, cancellationToken);

    public Task PublishCountdownSyncAsync(
        TransactionRealtimePayloads.CountdownSync payload,
        CancellationToken cancellationToken) =>
        SendAsync(payload.TransactionId, CountdownSyncEvent, payload, cancellationToken);

    public Task PublishPaymentDetectedAsync(
        TransactionRealtimePayloads.PaymentDetected payload,
        CancellationToken cancellationToken) =>
        SendAsync(payload.TransactionId, PaymentDetectedEvent, payload, cancellationToken);

    public Task PublishPaymentConfirmedAsync(
        TransactionRealtimePayloads.PaymentConfirmed payload,
        CancellationToken cancellationToken) =>
        SendAsync(payload.TransactionId, PaymentConfirmedEvent, payload, cancellationToken);

    public Task PublishDisputeUpdateAsync(
        TransactionRealtimePayloads.DisputeUpdate payload,
        CancellationToken cancellationToken) =>
        SendAsync(payload.TransactionId, DisputeUpdateEvent, payload, cancellationToken);

    public Task PublishFlagResolvedAsync(
        TransactionRealtimePayloads.FlagResolved payload,
        CancellationToken cancellationToken) =>
        SendAsync(payload.TransactionId, FlagResolvedEvent, payload, cancellationToken);

    public Task PublishEmergencyHoldAppliedAsync(
        TransactionRealtimePayloads.EmergencyHoldApplied payload,
        CancellationToken cancellationToken) =>
        SendAsync(payload.TransactionId, EmergencyHoldAppliedEvent, payload, cancellationToken);

    public Task PublishEmergencyHoldReleasedAsync(
        TransactionRealtimePayloads.EmergencyHoldReleased payload,
        CancellationToken cancellationToken) =>
        SendAsync(payload.TransactionId, EmergencyHoldReleasedEvent, payload, cancellationToken);

    private async Task SendAsync(
        Guid transactionId,
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await _hub.Clients
                .Group(TransactionsHub.GroupName(transactionId))
                .SendAsync(method, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            // Realtime delivery is best-effort — frontend re-fetches on
            // reconnect (T96), so the consumer must not surface this as a
            // redelivery trigger to the outbox dispatcher.
            _logger.LogWarning(
                ex,
                "SignalR push failed for transaction {TransactionId} method {Method}.",
                transactionId, method);
        }
    }
}
