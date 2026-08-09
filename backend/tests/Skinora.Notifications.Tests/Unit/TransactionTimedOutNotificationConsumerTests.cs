using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// Unit coverage for <see cref="TransactionTimedOutNotificationConsumer"/>
/// (T49 — 02 §3.2, 03 §4.1–§4.4). Verifies per-recipient fan-out, the
/// phase × role reason text taken verbatim from 03 §4.1–§4.4 and the
/// consumer-idempotency contract inherited from
/// <see cref="NotificationConsumerBase{TEvent}"/>.
/// </summary>
public class TransactionTimedOutNotificationConsumerTests
{
    [Fact]
    public async Task Handle_Emits_Per_Party_Requests_For_Accept_Phase()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new TransactionTimedOutNotificationConsumer(
            dispatcher, processed,
            NullLogger<TransactionTimedOutNotificationConsumer>.Instance);

        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var domainEvent = new TransactionTimedOutEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            Phase: TimeoutPhase.Accept,
            SellerId: sellerId,
            BuyerId: buyerId,
            ItemName: "AK-47",
            FromStatus: TransactionStatus.CREATED,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Equal(2, dispatcher.Requests.Count);
        var seller = Assert.Single(dispatcher.Requests, r => r.UserId == sellerId);
        var buyer = Assert.Single(dispatcher.Requests, r => r.UserId == buyerId);

        Assert.All(dispatcher.Requests, r =>
        {
            Assert.Equal(NotificationType.TRANSACTION_CANCELLED, r.Type);
            Assert.Equal(domainEvent.TransactionId, r.TransactionId);
            Assert.Equal("AK-47", r.Parameters["ItemName"]);
        });

        Assert.Equal("Alıcı zamanında kabul etmedi, işlem iptal oldu", seller.Parameters["Reason"]);
        Assert.Equal("İşlem zaman aşımı nedeniyle iptal oldu", buyer.Parameters["Reason"]);
    }

    [Fact]
    public async Task Handle_Skips_Buyer_When_Not_Registered()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new TransactionTimedOutNotificationConsumer(
            dispatcher, processed,
            NullLogger<TransactionTimedOutNotificationConsumer>.Instance);

        var domainEvent = new TransactionTimedOutEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            Phase: TimeoutPhase.Accept,
            SellerId: Guid.NewGuid(),
            BuyerId: null,
            ItemName: "AWP",
            FromStatus: TransactionStatus.CREATED,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(domainEvent.SellerId, request.UserId);
    }

    [Theory]
    [InlineData(TimeoutPhase.Accept,
        "Alıcı zamanında kabul etmedi, işlem iptal oldu",
        "İşlem zaman aşımı nedeniyle iptal oldu")]
    [InlineData(TimeoutPhase.SellerConfirm,
        "Zamanında onay vermediniz, işlem iptal oldu",
        "Satıcı işleme devam etmedi, işlem iptal oldu")]
    // v3.0 — no item refund text: the item never left the seller's inventory.
    [InlineData(TimeoutPhase.Payment,
        "Alıcı ödeme yapmadı, işlem iptal oldu",
        "Zamanında ödeme yapılmadı, işlem iptal oldu")]
    // v3.0 — the delivery phase is the SELLER's responsibility now (02 §3.1);
    // the reason text swapped sides with it.
    [InlineData(TimeoutPhase.Delivery,
        "Item'ı zamanında göndermediniz, işlem iptal oldu ve ödeme alıcıya iade edildi",
        "Satıcı item'ı göndermedi, ödemeniz iade edildi")]
    public async Task Handle_Emits_Phase_Specific_Reason_Text(
        TimeoutPhase phase, string sellerReason, string buyerReason)
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new TransactionTimedOutNotificationConsumer(
            dispatcher, processed,
            NullLogger<TransactionTimedOutNotificationConsumer>.Instance);

        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var domainEvent = new TransactionTimedOutEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            Phase: phase,
            SellerId: sellerId,
            BuyerId: buyerId,
            ItemName: "M4A1",
            FromStatus: TransactionStatus.CREATED,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Equal(sellerReason, dispatcher.Requests.Single(r => r.UserId == sellerId).Parameters["Reason"]);
        Assert.Equal(buyerReason, dispatcher.Requests.Single(r => r.UserId == buyerId).Parameters["Reason"]);
    }

    [Fact]
    public async Task Handle_Idempotent_When_EventAlreadyProcessed()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new TransactionTimedOutNotificationConsumer(
            dispatcher, processed,
            NullLogger<TransactionTimedOutNotificationConsumer>.Instance);

        var domainEvent = new TransactionTimedOutEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            Phase: TimeoutPhase.Payment,
            SellerId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            ItemName: "Knife",
            FromStatus: TransactionStatus.SELLER_CONFIRMED,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Equal(2, dispatcher.Requests.Count);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Equal(2, dispatcher.Requests.Count);

        Assert.True(await processed.ExistsAsync(domainEvent.EventId, "notifications.transaction-timed-out"));
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
