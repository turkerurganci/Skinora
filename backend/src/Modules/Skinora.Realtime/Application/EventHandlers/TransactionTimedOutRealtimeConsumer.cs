using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes <c>TransactionStatusChanged(FromStatus → CANCELLED_TIMEOUT)</c> on
/// <c>/hubs/transactions</c> when any of the lifecycle deadlines elapse
/// (T49 — 07 §11.1, 03 §4.1–§4.4). The producer
/// (<c>TimeoutSideEffectPublisher</c>) snapshots <c>FromStatus</c> alongside
/// <c>Phase</c>, so the consumer can publish without an extra DB read.
/// </summary>
public sealed class TransactionTimedOutRealtimeConsumer
    : RealtimeConsumerBase<TransactionTimedOutEvent>
{
    private readonly ITransactionRealtimePublisher _publisher;

    public TransactionTimedOutRealtimeConsumer(
        ITransactionRealtimePublisher publisher,
        IProcessedEventStore processedEventStore,
        ILogger<TransactionTimedOutRealtimeConsumer> logger)
        : base(processedEventStore, logger)
    {
        _publisher = publisher;
    }

    protected override string ConsumerName => "realtime.transaction-timed-out";

    protected override Task PublishAsync(
        TransactionTimedOutEvent domainEvent,
        CancellationToken cancellationToken) =>
        _publisher.PublishStatusChangedAsync(
            new TransactionRealtimePayloads.TransactionStatusChanged(
                TransactionId: domainEvent.TransactionId,
                FromStatus: domainEvent.FromStatus,
                ToStatus: TransactionStatus.CANCELLED_TIMEOUT,
                Timestamp: domainEvent.OccurredAt),
            cancellationToken);
}
