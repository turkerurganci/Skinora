using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes <c>TransactionStatusChanged</c> on <c>/hubs/transactions</c> for the
/// Steam orchestration transitions carried by the generic
/// <see cref="TransactionStatusChangedEvent"/> (WP9 — 07 §11.1, closes T61 K2):
/// ACCEPTED → TRADE_OFFER_SENT_TO_SELLER, TRADE_OFFER_SENT_TO_SELLER →
/// ITEM_ESCROWED, PAYMENT_RECEIVED → TRADE_OFFER_SENT_TO_BUYER and
/// TRADE_OFFER_SENT_TO_BUYER → ITEM_DELIVERED.
/// </summary>
/// <remarks>
/// The from/to status is carried verbatim by the producer (the dispatch job
/// and the Steam webhook handler capture it around the <c>Fire()</c>), so this
/// consumer is a pure relay with no DB lookup. Idempotency + best-effort
/// delivery are inherited from <see cref="RealtimeConsumerBase{TEvent}"/>.
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
