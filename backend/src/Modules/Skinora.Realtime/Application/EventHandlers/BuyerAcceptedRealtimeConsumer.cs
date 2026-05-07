using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes <c>TransactionStatusChanged(CREATED → ACCEPTED)</c> on
/// <c>/hubs/transactions</c> when the buyer accepts (T46 — 07 §11.1, §7.6).
/// </summary>
/// <remarks>
/// State-machine guard <c>HasFieldsForAccepted</c> means the
/// <c>BuyerAcceptedEvent</c> can only originate from a <c>CREATED → ACCEPTED</c>
/// transition (see <c>TransactionStateMachine.ConfigureTransitions</c>), so the
/// pre-transition state can be safely hardcoded here without a DB lookup.
/// </remarks>
public sealed class BuyerAcceptedRealtimeConsumer
    : RealtimeConsumerBase<BuyerAcceptedEvent>
{
    private readonly ITransactionRealtimePublisher _publisher;

    public BuyerAcceptedRealtimeConsumer(
        ITransactionRealtimePublisher publisher,
        IProcessedEventStore processedEventStore,
        ILogger<BuyerAcceptedRealtimeConsumer> logger)
        : base(processedEventStore, logger)
    {
        _publisher = publisher;
    }

    protected override string ConsumerName => "realtime.buyer-accepted";

    protected override Task PublishAsync(
        BuyerAcceptedEvent domainEvent,
        CancellationToken cancellationToken) =>
        _publisher.PublishStatusChangedAsync(
            new TransactionRealtimePayloads.TransactionStatusChanged(
                TransactionId: domainEvent.TransactionId,
                FromStatus: TransactionStatus.CREATED,
                ToStatus: TransactionStatus.ACCEPTED,
                Timestamp: domainEvent.OccurredAt),
            cancellationToken);
}
