using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes <c>FlagResolved(REJECTED)</c> plus
/// <c>TransactionStatusChanged(FLAGGED → CANCELLED_ADMIN)</c> on
/// <c>/hubs/transactions</c> after an admin rejects a transaction-scoped
/// fraud flag (T54 — 07 §11.1, §9.5, 03 §8.2). Account-level flags do not
/// produce a hub push (no per-transaction room).
/// </summary>
public sealed class FraudFlagRejectedRealtimeConsumer
    : RealtimeConsumerBase<FraudFlagRejectedEvent>
{
    private readonly ITransactionRealtimePublisher _publisher;

    public FraudFlagRejectedRealtimeConsumer(
        ITransactionRealtimePublisher publisher,
        IProcessedEventStore processedEventStore,
        ILogger<FraudFlagRejectedRealtimeConsumer> logger)
        : base(processedEventStore, logger)
    {
        _publisher = publisher;
    }

    protected override string ConsumerName => "realtime.fraud-flag-rejected";

    protected override async Task PublishAsync(
        FraudFlagRejectedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        if (domainEvent.TransactionId is not Guid transactionId)
        {
            return;
        }

        await _publisher.PublishFlagResolvedAsync(
            new TransactionRealtimePayloads.FlagResolved(
                TransactionId: transactionId,
                ReviewStatus: ReviewStatus.REJECTED),
            cancellationToken);

        await _publisher.PublishStatusChangedAsync(
            new TransactionRealtimePayloads.TransactionStatusChanged(
                TransactionId: transactionId,
                FromStatus: TransactionStatus.FLAGGED,
                ToStatus: TransactionStatus.CANCELLED_ADMIN,
                Timestamp: domainEvent.OccurredAt),
            cancellationToken);
    }
}
