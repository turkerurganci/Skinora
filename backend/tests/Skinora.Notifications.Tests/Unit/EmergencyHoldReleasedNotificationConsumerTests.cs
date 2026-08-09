using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// Unit coverage for <see cref="EmergencyHoldReleasedNotificationConsumer"/>
/// (T59 — 07 §9.22, 02 §7, 03 §8.8). Verifies RESUME fan-out (both parties)
/// and CANCEL skip semantics (the cancel branch is owned by the existing
/// <see cref="TransactionCancelledNotificationConsumer"/>).
/// </summary>
public class EmergencyHoldReleasedNotificationConsumerTests
{
    [Fact]
    public async Task Handle_Resume_Notifies_Both_Parties()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new EmergencyHoldReleasedNotificationConsumer(
            dispatcher, processed,
            NullLogger<EmergencyHoldReleasedNotificationConsumer>.Instance);

        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var domainEvent = new EmergencyHoldReleasedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            SellerId: sellerId,
            BuyerId: buyerId,
            ItemName: "AWP | Asiimov",
            Action: EmergencyHoldReleaseAction.RESUME,
            ResumedStatus: TransactionStatus.PAYMENT_RECEIVED,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Equal(2, dispatcher.Requests.Count);
        Assert.Contains(dispatcher.Requests, r => r.UserId == sellerId);
        Assert.Contains(dispatcher.Requests, r => r.UserId == buyerId);
        Assert.All(dispatcher.Requests, r =>
        {
            Assert.Equal(NotificationType.EMERGENCY_HOLD_RELEASED, r.Type);
            Assert.Equal("AWP | Asiimov", r.Parameters["ItemName"]);
            Assert.Equal(nameof(TransactionStatus.PAYMENT_RECEIVED), r.Parameters["ResumedStatus"]);
        });
    }

    [Fact]
    public async Task Handle_Cancel_Action_Skips_Notifications()
    {
        // CANCEL branch is owned by TransactionCancelledNotificationConsumer
        // (CancelledByType.ADMIN). The released-event consumer must not fan
        // out a duplicate "review completed" notice.
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new EmergencyHoldReleasedNotificationConsumer(
            dispatcher, processed,
            NullLogger<EmergencyHoldReleasedNotificationConsumer>.Instance);

        var domainEvent = new EmergencyHoldReleasedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            ItemName: "Karambit",
            Action: EmergencyHoldReleaseAction.CANCEL,
            ResumedStatus: TransactionStatus.CANCELLED_ADMIN,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Empty(dispatcher.Requests);
        // Idempotency marker is still written so a replay does not re-evaluate.
        Assert.True(await processed.ExistsAsync(
            domainEvent.EventId, "notifications.emergency-hold-released"));
    }

    [Fact]
    public async Task Handle_Resume_Without_Buyer_Notifies_Only_Seller()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new EmergencyHoldReleasedNotificationConsumer(
            dispatcher, processed,
            NullLogger<EmergencyHoldReleasedNotificationConsumer>.Instance);

        var sellerId = Guid.NewGuid();
        var domainEvent = new EmergencyHoldReleasedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            SellerId: sellerId,
            BuyerId: null,
            ItemName: "M9 Bayonet",
            Action: EmergencyHoldReleaseAction.RESUME,
            ResumedStatus: TransactionStatus.CREATED,
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
        var sut = new EmergencyHoldReleasedNotificationConsumer(
            dispatcher, processed,
            NullLogger<EmergencyHoldReleasedNotificationConsumer>.Instance);

        var domainEvent = new EmergencyHoldReleasedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            ItemName: "Driver Gloves",
            Action: EmergencyHoldReleaseAction.RESUME,
            ResumedStatus: TransactionStatus.SELLER_CONFIRMED,
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
