using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// Unit coverage for <see cref="EmergencyHoldAppliedNotificationConsumer"/>
/// (T59 — 07 §9.21, 02 §7, 03 §8.8). Verifies seller + buyer fan-out, the
/// pre-accept (null buyer) carve-out, and the consumer-idempotency contract
/// inherited from <see cref="NotificationConsumerBase{TEvent}"/>.
/// </summary>
public class EmergencyHoldAppliedNotificationConsumerTests
{
    [Fact]
    public async Task Handle_Notifies_Both_Parties_When_Buyer_Registered()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new EmergencyHoldAppliedNotificationConsumer(
            dispatcher, processed,
            NullLogger<EmergencyHoldAppliedNotificationConsumer>.Instance);

        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var domainEvent = new EmergencyHoldAppliedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            SellerId: sellerId,
            BuyerId: buyerId,
            ItemName: "AK-47 | Vulcan",
            Reason: "Sanctions screening",
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Equal(2, dispatcher.Requests.Count);
        Assert.Contains(dispatcher.Requests, r => r.UserId == sellerId);
        Assert.Contains(dispatcher.Requests, r => r.UserId == buyerId);
        Assert.All(dispatcher.Requests, r =>
        {
            Assert.Equal(NotificationType.EMERGENCY_HOLD_APPLIED, r.Type);
            Assert.Equal(domainEvent.TransactionId, r.TransactionId);
            Assert.Equal("AK-47 | Vulcan", r.Parameters["ItemName"]);
            Assert.Equal("Sanctions screening", r.Parameters["Reason"]);
        });
    }

    [Fact]
    public async Task Handle_Without_Buyer_Notifies_Only_Seller()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new EmergencyHoldAppliedNotificationConsumer(
            dispatcher, processed,
            NullLogger<EmergencyHoldAppliedNotificationConsumer>.Instance);

        var sellerId = Guid.NewGuid();
        var domainEvent = new EmergencyHoldAppliedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            SellerId: sellerId,
            BuyerId: null,
            ItemName: "Karambit",
            Reason: "Pre-accept compliance hold",
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(sellerId, request.UserId);
    }

    [Fact]
    public async Task Handle_Idempotent_When_EventAlreadyProcessed()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new EmergencyHoldAppliedNotificationConsumer(
            dispatcher, processed,
            NullLogger<EmergencyHoldAppliedNotificationConsumer>.Instance);

        var domainEvent = new EmergencyHoldAppliedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            ItemName: "AWP",
            Reason: "Replay test",
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Equal(2, dispatcher.Requests.Count);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Equal(2, dispatcher.Requests.Count);
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
