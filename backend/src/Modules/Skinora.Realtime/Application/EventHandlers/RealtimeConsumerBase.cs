using MediatR;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Domain;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Reusable base for MediatR-style consumers that translate a
/// <typeparamref name="TEvent"/> domain event into one or more
/// <c>/hubs/transactions</c> RT1 pushes (T61 — 07 §11.1).
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>NotificationConsumerBase</c> from the Notifications module
/// (T37): consumer-side idempotency goes through
/// <see cref="IProcessedEventStore"/> using <see cref="ConsumerName"/> as the
/// second key so an outbox-replayed row never produces duplicate pushes.
/// </para>
/// <para>
/// Realtime delivery itself is best-effort (see
/// <c>SignalRTransactionRealtimePublisher</c>) — transport errors are logged
/// and swallowed so the outbox dispatcher does not retry purely because no
/// subscriber was listening. Frontend reconciliation is T96's responsibility
/// (re-fetch detail page on reconnect).
/// </para>
/// </remarks>
public abstract class RealtimeConsumerBase<TEvent> : INotificationHandler<TEvent>
    where TEvent : IDomainEvent
{
    private readonly IProcessedEventStore _processedEventStore;
    private readonly ILogger _logger;

    protected RealtimeConsumerBase(
        IProcessedEventStore processedEventStore,
        ILogger logger)
    {
        _processedEventStore = processedEventStore;
        _logger = logger;
    }

    /// <summary>
    /// Stable consumer identifier used as the second key in
    /// <c>ProcessedEvents</c>. Pick something unique per concrete handler
    /// (e.g. <c>"realtime.buyer-accepted"</c>).
    /// </summary>
    protected abstract string ConsumerName { get; }

    /// <summary>Performs the actual realtime push(es) for the event.</summary>
    protected abstract Task PublishAsync(TEvent domainEvent, CancellationToken cancellationToken);

    public async Task Handle(TEvent notification, CancellationToken cancellationToken)
    {
        if (await _processedEventStore.ExistsAsync(notification.EventId, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Realtime consumer {Consumer} already processed event {EventId}; skipping.",
                ConsumerName, notification.EventId);
            return;
        }

        await PublishAsync(notification, cancellationToken);

        await _processedEventStore.MarkAsProcessedAsync(
            notification.EventId, ConsumerName, cancellationToken);
    }
}
