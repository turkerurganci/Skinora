using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Tests.TestSupport;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// Unit coverage for <see cref="LatePaymentRefundRequestedNotificationConsumer"/>
/// (T110 — 03 §5.4 "Gecikmeli ödeme"). Verifies the single buyer-targeted
/// notification, the LATE_PAYMENT_REFUNDED type + Amount parameter, and the
/// consumer-idempotency contract inherited from
/// <see cref="NotificationConsumerBase{TEvent}"/>.
/// </summary>
public class LatePaymentRefundRequestedNotificationConsumerTests
{
    private static LatePaymentRefundRequestedEvent CreateEvent(Guid buyerId, Guid transactionId) =>
        new(
            EventId: Guid.NewGuid(),
            TransactionId: transactionId,
            BuyerId: buyerId,
            RefundTransactionId: Guid.NewGuid(),
            ReceivedAmount: 42.5m,
            Stablecoin: StablecoinType.USDT,
            SourceAddress: "TBuyerSourceWallet00000000000000000",
            TxHash: "0xlatepayment",
            MonitorState: MonitoringStatus.POST_CANCEL_24H,
            OccurredAt: DateTime.UtcNow);

    [Fact]
    public async Task Handle_Notifies_Only_Buyer_With_LatePaymentRefunded()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new LatePaymentRefundRequestedNotificationConsumer(
            dispatcher, processed,
            NullLogger<LatePaymentRefundRequestedNotificationConsumer>.Instance);

        var buyerId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var domainEvent = CreateEvent(buyerId, transactionId);

        await sut.Handle(domainEvent, CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(buyerId, request.UserId);
        Assert.Equal(NotificationType.LATE_PAYMENT_REFUNDED, request.Type);
        Assert.Equal(transactionId, request.TransactionId);
        Assert.Equal("42.5", request.Parameters["Amount"]);
        Assert.Equal("USDT", request.Parameters["Stablecoin"]);
    }

    [Fact]
    public async Task Handle_Idempotent_When_EventAlreadyProcessed()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new LatePaymentRefundRequestedNotificationConsumer(
            dispatcher, processed,
            NullLogger<LatePaymentRefundRequestedNotificationConsumer>.Instance);

        var domainEvent = CreateEvent(Guid.NewGuid(), Guid.NewGuid());

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Single(dispatcher.Requests);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Single(dispatcher.Requests);
    }
}
