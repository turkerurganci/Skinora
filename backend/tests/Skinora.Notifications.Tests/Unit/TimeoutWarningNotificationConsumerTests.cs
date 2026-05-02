using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// Unit coverage for <see cref="TimeoutWarningNotificationConsumer"/> (T48).
/// Verifies the event → request translation (recipient, type, parameters) and
/// the consumer-idempotency contract inherited from
/// <see cref="NotificationConsumerBase{TEvent}"/>.
/// </summary>
public class TimeoutWarningNotificationConsumerTests
{
    [Fact]
    public async Task Handle_TranslatesEventToTimeoutWarningRequest()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new TimeoutWarningNotificationConsumer(
            dispatcher, processed,
            NullLogger<TimeoutWarningNotificationConsumer>.Instance);

        var buyerId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);
        var domainEvent = new TimeoutWarningEvent(
            EventId: Guid.NewGuid(),
            TransactionId: transactionId,
            RecipientUserId: buyerId,
            ItemName: "AK-47 | Redline",
            RemainingMinutes: 15,
            OccurredAt: occurredAt);

        await sut.Handle(domainEvent, CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(buyerId, request.UserId);
        Assert.Equal(NotificationType.TIMEOUT_WARNING, request.Type);
        Assert.Equal(transactionId, request.TransactionId);
        Assert.Equal("AK-47 | Redline", request.Parameters["ItemName"]);
        Assert.Equal("15", request.Parameters["RemainingMinutes"]);
    }

    [Fact]
    public async Task Handle_SkipsDispatch_When_EventAlreadyProcessed()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new TimeoutWarningNotificationConsumer(
            dispatcher, processed,
            NullLogger<TimeoutWarningNotificationConsumer>.Instance);

        var domainEvent = new TimeoutWarningEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            RecipientUserId: Guid.NewGuid(),
            ItemName: "AWP",
            RemainingMinutes: 30,
            OccurredAt: DateTime.UtcNow);

        // First Handle marks the event as processed.
        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Single(dispatcher.Requests);

        // Replaying the same event must short-circuit before DispatchAsync.
        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Single(dispatcher.Requests);
    }

    [Fact]
    public async Task Handle_MarksEventProcessed_Under_StableConsumerName()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new TimeoutWarningNotificationConsumer(
            dispatcher, processed,
            NullLogger<TimeoutWarningNotificationConsumer>.Instance);

        var eventId = Guid.NewGuid();
        var domainEvent = new TimeoutWarningEvent(
            EventId: eventId,
            TransactionId: Guid.NewGuid(),
            RecipientUserId: Guid.NewGuid(),
            ItemName: "M4A1-S",
            RemainingMinutes: 5,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.True(await processed.ExistsAsync(eventId, "notifications.timeout-warning"));
        Assert.False(await processed.ExistsAsync(eventId, "notifications.some-other-consumer"));
    }

    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        public List<NotificationRequest> Requests { get; } = [];

        public Task DispatchAsync(NotificationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryProcessedEventStore : IProcessedEventStore
    {
        private readonly HashSet<(Guid eventId, string consumer)> _entries = new();

        public Task<bool> ExistsAsync(
            Guid eventId, string consumerName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_entries.Contains((eventId, consumerName)));

        public Task MarkAsProcessedAsync(
            Guid eventId, string consumerName,
            CancellationToken cancellationToken = default)
        {
            _entries.Add((eventId, consumerName));
            return Task.CompletedTask;
        }
    }
}
