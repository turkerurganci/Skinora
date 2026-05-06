using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes <c>DisputeUpdate</c> on <c>/hubs/transactions</c> when a dispute is
/// escalated — either by the buyer (07 §7.10) or by the WRONG_ITEM auto-checker
/// (03 §6.3). Both paths land here because the consumer pushes the same
/// <c>ESCALATED</c> status update; the auto-resolution hand-off message is
/// surfaced in <see cref="TransactionRealtimePayloads.DisputeUpdate.AutoCheckResult"/>
/// when the auto-checker fired the escalation.
/// </summary>
public sealed class DisputeEscalatedRealtimeConsumer
    : RealtimeConsumerBase<DisputeEscalatedEvent>
{
    private readonly ITransactionRealtimePublisher _publisher;

    public DisputeEscalatedRealtimeConsumer(
        ITransactionRealtimePublisher publisher,
        IProcessedEventStore processedEventStore,
        ILogger<DisputeEscalatedRealtimeConsumer> logger)
        : base(processedEventStore, logger)
    {
        _publisher = publisher;
    }

    protected override string ConsumerName => "realtime.dispute-escalated";

    protected override Task PublishAsync(
        DisputeEscalatedEvent domainEvent,
        CancellationToken cancellationToken) =>
        _publisher.PublishDisputeUpdateAsync(
            new TransactionRealtimePayloads.DisputeUpdate(
                TransactionId: domainEvent.TransactionId,
                DisputeId: domainEvent.DisputeId,
                Status: DisputeStatus.ESCALATED,
                AutoCheckResult: domainEvent.AutoEscalated ? "AUTO_WRONG_ITEM" : null),
            cancellationToken);
}
