using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes <c>FlagResolved(APPROVED)</c> plus
/// <c>TransactionStatusChanged(FLAGGED → CREATED)</c> on
/// <c>/hubs/transactions</c> after an admin approves a transaction-scoped
/// fraud flag (T54 — 07 §11.1, §9.4, 03 §8.2). Account-level flags
/// (<see cref="FraudFlagApprovedEvent.TransactionId"/> = <c>null</c>) do not
/// produce a hub push because there is no per-transaction room to address.
/// </summary>
public sealed class FraudFlagApprovedRealtimeConsumer
    : RealtimeConsumerBase<FraudFlagApprovedEvent>
{
    private readonly ITransactionRealtimePublisher _publisher;

    public FraudFlagApprovedRealtimeConsumer(
        ITransactionRealtimePublisher publisher,
        IProcessedEventStore processedEventStore,
        ILogger<FraudFlagApprovedRealtimeConsumer> logger)
        : base(processedEventStore, logger)
    {
        _publisher = publisher;
    }

    protected override string ConsumerName => "realtime.fraud-flag-approved";

    protected override async Task PublishAsync(
        FraudFlagApprovedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        if (domainEvent.TransactionId is not Guid transactionId)
        {
            return;
        }

        await _publisher.PublishFlagResolvedAsync(
            new TransactionRealtimePayloads.FlagResolved(
                TransactionId: transactionId,
                ReviewStatus: ReviewStatus.APPROVED),
            cancellationToken);

        await _publisher.PublishStatusChangedAsync(
            new TransactionRealtimePayloads.TransactionStatusChanged(
                TransactionId: transactionId,
                FromStatus: TransactionStatus.FLAGGED,
                ToStatus: TransactionStatus.CREATED,
                Timestamp: domainEvent.OccurredAt),
            cancellationToken);
    }
}
