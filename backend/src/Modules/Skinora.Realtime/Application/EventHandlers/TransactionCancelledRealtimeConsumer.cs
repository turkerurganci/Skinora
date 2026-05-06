using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes <c>TransactionStatusChanged(FromStatus → CANCELLED_*)</c> on
/// <c>/hubs/transactions</c> for user-, admin- or buyer-initiated cancels
/// (T51 — 07 §11.1, §7.7). The state-machine destination is derived from
/// <see cref="TransactionCancelledEvent.CancelledBy"/>; <c>FromStatus</c> is
/// the snapshot captured by the producer (T61 — event contract enrichment).
/// </summary>
public sealed class TransactionCancelledRealtimeConsumer
    : RealtimeConsumerBase<TransactionCancelledEvent>
{
    private readonly ITransactionRealtimePublisher _publisher;

    public TransactionCancelledRealtimeConsumer(
        ITransactionRealtimePublisher publisher,
        IProcessedEventStore processedEventStore,
        ILogger<TransactionCancelledRealtimeConsumer> logger)
        : base(processedEventStore, logger)
    {
        _publisher = publisher;
    }

    protected override string ConsumerName => "realtime.transaction-cancelled";

    protected override Task PublishAsync(
        TransactionCancelledEvent domainEvent,
        CancellationToken cancellationToken) =>
        _publisher.PublishStatusChangedAsync(
            new TransactionRealtimePayloads.TransactionStatusChanged(
                TransactionId: domainEvent.TransactionId,
                FromStatus: domainEvent.FromStatus,
                ToStatus: ToStatusFor(domainEvent.CancelledBy),
                Timestamp: domainEvent.OccurredAt),
            cancellationToken);

    private static TransactionStatus ToStatusFor(CancelledByType cancelledBy) => cancelledBy switch
    {
        CancelledByType.SELLER => TransactionStatus.CANCELLED_SELLER,
        CancelledByType.BUYER => TransactionStatus.CANCELLED_BUYER,
        CancelledByType.ADMIN => TransactionStatus.CANCELLED_ADMIN,
        CancelledByType.TIMEOUT => TransactionStatus.CANCELLED_TIMEOUT,
        _ => throw new ArgumentOutOfRangeException(
            nameof(cancelledBy), cancelledBy, "Unknown CancelledByType."),
    };
}
