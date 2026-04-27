using MediatR;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Domain;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Reusable base class for MediatR-style consumers that translate a
/// <typeparamref name="TEvent"/> domain event into one or more
/// <see cref="NotificationRequest"/> instances and dispatch them through
/// <see cref="INotificationDispatcher"/>.
/// </summary>
/// <typeparam name="TEvent">Concrete domain event the derived class consumes.</typeparam>
/// <remarks>
/// <para>
/// Subclasses implement <see cref="BuildRequestsAsync"/> to fan a single
/// event out to every recipient (e.g. <c>TransactionCreatedEvent</c> →
/// invitation to buyer + audit notification to seller). The base wires up
/// consumer-side idempotency via <see cref="IProcessedEventStore"/> using
/// the consumer name reported by <see cref="ConsumerName"/>, so a replayed
/// outbox row never produces duplicate notifications (05 §5.1, 09 §9.3
/// "Consumer Idempotency").
/// </para>
/// <para>
/// The base does not call <c>SaveChangesAsync</c>: the outer outbox
/// dispatcher commits all pending changes (notification rows, delivery
/// rows, processed-event row, outbox status update) in a single transaction
/// at the end of its batch. This matches the contract documented on
/// <see cref="INotificationDispatcher"/>.
/// </para>
/// <para>
/// T37 ships this base class as the recommended pattern for T44+ event
/// handlers; no production handler subclass is registered here yet because
/// the corresponding domain events arrive with later tasks
/// (T44 transaction state machine, T58 dispute, etc.).
/// </para>
/// </remarks>
public abstract class NotificationConsumerBase<TEvent> : INotificationHandler<TEvent>
    where TEvent : IDomainEvent
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly IProcessedEventStore _processedEventStore;
    private readonly ILogger _logger;

    protected NotificationConsumerBase(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger logger)
    {
        _dispatcher = dispatcher;
        _processedEventStore = processedEventStore;
        _logger = logger;
    }

    /// <summary>
    /// Stable consumer identifier used as the second key in the
    /// <c>ProcessedEvents</c> table. Pick something unique per concrete
    /// handler (e.g. <c>"notifications.transaction-invite"</c>).
    /// </summary>
    protected abstract string ConsumerName { get; }

    /// <summary>
    /// Translates a concrete event into the set of notification requests it
    /// should produce. Returning an empty enumerable is valid (e.g. when the
    /// event affects only system-internal state).
    /// </summary>
    protected abstract Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        TEvent domainEvent,
        CancellationToken cancellationToken);

    public async Task Handle(TEvent notification, CancellationToken cancellationToken)
    {
        if (await _processedEventStore.ExistsAsync(notification.EventId, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Notification consumer {Consumer} already processed event {EventId}; skipping.",
                ConsumerName,
                notification.EventId);
            return;
        }

        var requests = await BuildRequestsAsync(notification, cancellationToken);

        foreach (var request in requests)
        {
            await _dispatcher.DispatchAsync(request, cancellationToken);
        }

        await _processedEventStore.MarkAsProcessedAsync(
            notification.EventId,
            ConsumerName,
            cancellationToken);
    }
}
