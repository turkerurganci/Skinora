using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes <c>EmergencyHoldApplied</c> on <c>/hubs/transactions</c> after an
/// admin freezes a transaction (T59 — 07 §11.1, §9.21). The status itself does
/// not change (emergency hold is an overlay flag, not a state transition), so
/// no <c>TransactionStatusChanged</c> push is emitted; clients use the
/// <c>frozen</c> flag in subsequent <c>CountdownSync</c> events.
/// </summary>
public sealed class EmergencyHoldAppliedRealtimeConsumer
    : RealtimeConsumerBase<EmergencyHoldAppliedEvent>
{
    private readonly ITransactionRealtimePublisher _publisher;

    public EmergencyHoldAppliedRealtimeConsumer(
        ITransactionRealtimePublisher publisher,
        IProcessedEventStore processedEventStore,
        ILogger<EmergencyHoldAppliedRealtimeConsumer> logger)
        : base(processedEventStore, logger)
    {
        _publisher = publisher;
    }

    protected override string ConsumerName => "realtime.emergency-hold-applied";

    protected override Task PublishAsync(
        EmergencyHoldAppliedEvent domainEvent,
        CancellationToken cancellationToken) =>
        _publisher.PublishEmergencyHoldAppliedAsync(
            new TransactionRealtimePayloads.EmergencyHoldApplied(
                TransactionId: domainEvent.TransactionId,
                Message: domainEvent.Reason),
            cancellationToken);
}
