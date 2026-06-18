using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes <c>TransactionStatusChanged(ITEM_DELIVERED → COMPLETED)</c> on
/// <c>/hubs/transactions</c> when the WP1 payout-completion leg finalises a
/// transaction (WP9 — 07 §11.1). Reuses WP1's <see cref="PayoutCompletedEvent"/>
/// (raised when a SELLER_PAYOUT row confirms on chain) rather than a dedicated
/// push, so the <c>Complete</c> transition surfaces in realtime alongside the
/// seller "Ödemeniz gönderildi" notification.
/// </summary>
/// <remarks>
/// The <c>Complete</c> trigger is only valid from ITEM_DELIVERED (state-machine
/// guard), so the pre-transition status is hardcoded. This handler runs
/// independently of <c>PayoutCompletedConsumer</c> (the Transactions-module
/// handler that fires <c>Complete</c>); MediatR fans the outbox-dispatched
/// event out to both, each with its own idempotency key.
/// </remarks>
public sealed class PayoutCompletedRealtimeConsumer
    : RealtimeConsumerBase<PayoutCompletedEvent>
{
    private readonly ITransactionRealtimePublisher _publisher;

    public PayoutCompletedRealtimeConsumer(
        ITransactionRealtimePublisher publisher,
        IProcessedEventStore processedEventStore,
        ILogger<PayoutCompletedRealtimeConsumer> logger)
        : base(processedEventStore, logger)
    {
        _publisher = publisher;
    }

    protected override string ConsumerName => "realtime.payout-completed";

    protected override Task PublishAsync(
        PayoutCompletedEvent domainEvent,
        CancellationToken cancellationToken) =>
        _publisher.PublishStatusChangedAsync(
            new TransactionRealtimePayloads.TransactionStatusChanged(
                TransactionId: domainEvent.TransactionId,
                FromStatus: TransactionStatus.ITEM_DELIVERED,
                ToStatus: TransactionStatus.COMPLETED,
                Timestamp: domainEvent.OccurredAt),
            cancellationToken);
}
