using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// Unit coverage for <see cref="TransactionCancelledNotificationConsumer"/>
/// (T51 — 02 §7, 03 §2.5 / §3.3, 07 §7.7). Verifies the role-aware
/// counter-party fan-out: seller-cancel → buyer; buyer-cancel → seller; the
/// cancelling party never gets notified, and the consumer-idempotency contract
/// inherited from <see cref="NotificationConsumerBase{TEvent}"/>.
/// </summary>
public class TransactionCancelledNotificationConsumerTests
{
    [Fact]
    public async Task Handle_Seller_Cancel_Notifies_Only_Buyer()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new TransactionCancelledNotificationConsumer(
            dispatcher, processed,
            NullLogger<TransactionCancelledNotificationConsumer>.Instance);

        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var domainEvent = new TransactionCancelledEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            CancelledBy: CancelledByType.SELLER,
            SellerId: sellerId,
            BuyerId: buyerId,
            ItemName: "AK-47 | Redline",
            CancelReason: "Fiyat değişti",
            FromStatus: TransactionStatus.CREATED,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(buyerId, request.UserId);
        Assert.Equal(NotificationType.TRANSACTION_CANCELLED, request.Type);
        Assert.Equal(domainEvent.TransactionId, request.TransactionId);
        Assert.Equal("AK-47 | Redline", request.Parameters["ItemName"]);
        Assert.Equal("İşlem satıcı tarafından iptal edildi", request.Parameters["Reason"]);
    }

    [Fact]
    public async Task Handle_Buyer_Cancel_Notifies_Only_Seller()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new TransactionCancelledNotificationConsumer(
            dispatcher, processed,
            NullLogger<TransactionCancelledNotificationConsumer>.Instance);

        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var domainEvent = new TransactionCancelledEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            CancelledBy: CancelledByType.BUYER,
            SellerId: sellerId,
            BuyerId: buyerId,
            ItemName: "AWP | Asiimov",
            CancelReason: "Vazgeçtim",
            FromStatus: TransactionStatus.ACCEPTED,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(sellerId, request.UserId);
        Assert.Equal("İşlem alıcı tarafından iptal edildi", request.Parameters["Reason"]);
    }

    [Fact]
    public async Task Handle_Seller_Cancel_With_Null_Buyer_Emits_No_Notification()
    {
        // Pre-accept seller cancel: BuyerId is null → no counter-party to
        // notify. The cancelling seller already saw the in-app outcome on the
        // response envelope, so this should be a no-op (zero requests).
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new TransactionCancelledNotificationConsumer(
            dispatcher, processed,
            NullLogger<TransactionCancelledNotificationConsumer>.Instance);

        var domainEvent = new TransactionCancelledEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            CancelledBy: CancelledByType.SELLER,
            SellerId: Guid.NewGuid(),
            BuyerId: null,
            ItemName: "M4A1-S",
            CancelReason: "Fikir değişti",
            FromStatus: TransactionStatus.CREATED,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Empty(dispatcher.Requests);
        Assert.True(await processed.ExistsAsync(domainEvent.EventId, "notifications.transaction-cancelled"));
    }

    [Fact]
    public async Task Handle_Admin_Cancel_Notifies_Both_Parties()
    {
        // T59 — admin cancel publishes TransactionCancelledEvent with
        // CancelledByType.ADMIN; the consumer fans the notification out to
        // BOTH seller and buyer (neither initiated the cancel).
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new TransactionCancelledNotificationConsumer(
            dispatcher, processed,
            NullLogger<TransactionCancelledNotificationConsumer>.Instance);

        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var domainEvent = new TransactionCancelledEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            CancelledBy: CancelledByType.ADMIN,
            SellerId: sellerId,
            BuyerId: buyerId,
            ItemName: "Bayonet | Doppler",
            CancelReason: "Yasal talep",
            FromStatus: TransactionStatus.PAYMENT_RECEIVED,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Equal(2, dispatcher.Requests.Count);
        Assert.Contains(dispatcher.Requests, r => r.UserId == sellerId);
        Assert.Contains(dispatcher.Requests, r => r.UserId == buyerId);
        Assert.All(dispatcher.Requests, r =>
        {
            Assert.Equal(NotificationType.TRANSACTION_CANCELLED, r.Type);
            Assert.Equal("İşlem yönetici tarafından iptal edildi", r.Parameters["Reason"]);
        });
    }

    [Fact]
    public async Task Handle_Admin_Cancel_With_Null_Buyer_Notifies_Only_Seller()
    {
        // Pre-accept admin cancel (rare but possible — admin acts before any
        // buyer accepts the invite). Only seller is notified.
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new TransactionCancelledNotificationConsumer(
            dispatcher, processed,
            NullLogger<TransactionCancelledNotificationConsumer>.Instance);

        var sellerId = Guid.NewGuid();
        var domainEvent = new TransactionCancelledEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            CancelledBy: CancelledByType.ADMIN,
            SellerId: sellerId,
            BuyerId: null,
            ItemName: "M9 Bayonet",
            CancelReason: "Compliance",
            FromStatus: TransactionStatus.CREATED,
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
        var sut = new TransactionCancelledNotificationConsumer(
            dispatcher, processed,
            NullLogger<TransactionCancelledNotificationConsumer>.Instance);

        var domainEvent = new TransactionCancelledEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            CancelledBy: CancelledByType.BUYER,
            SellerId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            ItemName: "Karambit",
            CancelReason: "Tekrar etmeli",
            FromStatus: TransactionStatus.ACCEPTED,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Single(dispatcher.Requests);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Single(dispatcher.Requests);
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
