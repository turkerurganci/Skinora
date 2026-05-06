using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes <c>EmergencyHoldReleased</c> on <c>/hubs/transactions</c> after an
/// admin lifts an emergency hold via the RESUME path (T59 — 07 §11.1, §9.22).
/// CANCEL releases publish <see cref="TransactionCancelledEvent"/> instead so
/// the cancellation push pipeline already covers them.
/// </summary>
public sealed class EmergencyHoldReleasedRealtimeConsumer
    : RealtimeConsumerBase<EmergencyHoldReleasedEvent>
{
    private readonly ITransactionRealtimePublisher _publisher;

    public EmergencyHoldReleasedRealtimeConsumer(
        ITransactionRealtimePublisher publisher,
        IProcessedEventStore processedEventStore,
        ILogger<EmergencyHoldReleasedRealtimeConsumer> logger)
        : base(processedEventStore, logger)
    {
        _publisher = publisher;
    }

    protected override string ConsumerName => "realtime.emergency-hold-released";

    protected override Task PublishAsync(
        EmergencyHoldReleasedEvent domainEvent,
        CancellationToken cancellationToken) =>
        _publisher.PublishEmergencyHoldReleasedAsync(
            new TransactionRealtimePayloads.EmergencyHoldReleased(
                TransactionId: domainEvent.TransactionId,
                Action: domainEvent.Action,
                ResumedStatus: domainEvent.ResumedStatus),
            cancellationToken);
}
