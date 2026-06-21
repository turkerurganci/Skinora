using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Tests.TestSupport;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// Unit coverage for <see cref="TransactionInviteNotificationConsumer"/>
/// (WP19 — 03 §2.2 step 19, 02 §6.1). The consumer is event-only (no DB
/// re-query), so it is fully unit-testable: a registered buyer is invited; an
/// OPEN_LINK / unregistered buyer (null BuyerId) is a no-op; idempotency is
/// inherited from <see cref="NotificationConsumerBase{TEvent}"/>.
/// </summary>
public class TransactionInviteNotificationConsumerTests
{
    private static TransactionInviteNotificationConsumer CreateSut(
        RecordingNotificationDispatcher dispatcher,
        InMemoryProcessedEventStore processed)
        => new(dispatcher, processed, NullLogger<TransactionInviteNotificationConsumer>.Instance);

    private static TransactionCreatedEvent CreateEvent(Guid? buyerId) => new(
        EventId: Guid.NewGuid(),
        TransactionId: Guid.NewGuid(),
        SellerId: Guid.NewGuid(),
        BuyerId: buyerId,
        ItemName: "AK-47 | Redline",
        Price: 100m,
        Stablecoin: StablecoinType.USDT,
        OccurredAt: DateTime.UtcNow);

    [Fact]
    public async Task Handle_RegisteredBuyer_NotifiesBuyer()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = CreateSut(dispatcher, new InMemoryProcessedEventStore());

        var buyerId = Guid.NewGuid();
        var domainEvent = CreateEvent(buyerId);

        await sut.Handle(domainEvent, CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(buyerId, request.UserId);
        Assert.Equal(NotificationType.TRANSACTION_INVITE, request.Type);
        Assert.Equal(domainEvent.TransactionId, request.TransactionId);
        Assert.Equal("AK-47 | Redline", request.Parameters["ItemName"]);
        Assert.Equal("100", request.Parameters["Amount"]);
    }

    [Fact]
    public async Task Handle_OpenLinkOrUnregisteredBuyer_EmitsNoNotification()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = CreateSut(dispatcher, processed);

        var domainEvent = CreateEvent(buyerId: null);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Empty(dispatcher.Requests);
        // The event is still marked processed so a replay stays a no-op.
        Assert.True(await processed.ExistsAsync(domainEvent.EventId, "notifications.transaction-invite"));
    }

    [Fact]
    public async Task Handle_Idempotent_WhenEventAlreadyProcessed()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = CreateSut(dispatcher, new InMemoryProcessedEventStore());

        var domainEvent = CreateEvent(Guid.NewGuid());

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Single(dispatcher.Requests);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Single(dispatcher.Requests);
    }
}
