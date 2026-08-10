using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes <c>TransactionStatusChanged</c> on <c>/hubs/transactions</c> for the
/// forward-path transitions carried by the generic
/// <see cref="TransactionStatusChangedEvent"/> (WP9 — 07 §11.1, closes T61 K2).
/// Which legs those are is the producer's decision, not this consumer's: it
/// relays whatever status pair arrives.
/// </summary>
/// <remarks>
/// The from/to status is carried verbatim by the producer (captured around the
/// <c>Fire()</c>), so this consumer is a pure relay with no DB lookup.
/// Idempotency + best-effort delivery are inherited from
/// <see cref="RealtimeConsumerBase{TEvent}"/>. See
/// <see cref="TransactionStatusChangedEvent"/> for the v3.0 producer state.
/// </remarks>
public sealed class TransactionStatusChangedRealtimeConsumer
    : RealtimeConsumerBase<TransactionStatusChangedEvent>
{
    private readonly ITransactionRealtimePublisher _publisher;

    public TransactionStatusChangedRealtimeConsumer(
        ITransactionRealtimePublisher publisher,
        IProcessedEventStore processedEventStore,
        ILogger<TransactionStatusChangedRealtimeConsumer> logger)
        : base(processedEventStore, logger)
    {
        _publisher = publisher;
    }

    protected override string ConsumerName => "realtime.transaction-status-changed";

    protected override Task PublishAsync(
        TransactionStatusChangedEvent domainEvent,
        CancellationToken cancellationToken) =>
        _publisher.PublishStatusChangedAsync(
            new TransactionRealtimePayloads.TransactionStatusChanged(
                TransactionId: domainEvent.TransactionId,
                FromStatus: domainEvent.FromStatus,
                ToStatus: domainEvent.ToStatus,
                Timestamp: domainEvent.OccurredAt),
            cancellationToken);
}
