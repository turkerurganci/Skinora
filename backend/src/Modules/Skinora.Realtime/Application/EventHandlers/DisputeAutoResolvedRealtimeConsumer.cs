using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes <c>DisputeUpdate</c> on <c>/hubs/transactions</c> when an auto-check
/// closes a dispute without admin involvement (T58 — 07 §11.1, 03 §6.1/§6.2).
/// </summary>
public sealed class DisputeAutoResolvedRealtimeConsumer
    : RealtimeConsumerBase<DisputeAutoResolvedEvent>
{
    private readonly ITransactionRealtimePublisher _publisher;

    public DisputeAutoResolvedRealtimeConsumer(
        ITransactionRealtimePublisher publisher,
        IProcessedEventStore processedEventStore,
        ILogger<DisputeAutoResolvedRealtimeConsumer> logger)
        : base(processedEventStore, logger)
    {
        _publisher = publisher;
    }

    protected override string ConsumerName => "realtime.dispute-auto-resolved";

    protected override Task PublishAsync(
        DisputeAutoResolvedEvent domainEvent,
        CancellationToken cancellationToken) =>
        _publisher.PublishDisputeUpdateAsync(
            new TransactionRealtimePayloads.DisputeUpdate(
                TransactionId: domainEvent.TransactionId,
                DisputeId: domainEvent.DisputeId,
                Status: DisputeStatus.CLOSED,
                AutoCheckResult: domainEvent.Outcome),
            cancellationToken);
}
