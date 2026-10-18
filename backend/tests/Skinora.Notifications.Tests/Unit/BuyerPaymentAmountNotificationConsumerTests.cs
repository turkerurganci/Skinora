using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Resources;
using Skinora.Notifications.Tests.TestSupport;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// T144 — the wrong-amount notifications show the expected amount in the same
/// 2-decimal form as the transaction detail, and the received on-chain amount
/// exactly as it arrived (never rounded onto the expected one).
/// </summary>
public class BuyerPaymentAmountNotificationConsumerTests
{
    [Fact]
    public async Task Insufficient_ShowsExpectedTwoDecimal_And_ReceivedUnrounded()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new BuyerPaymentInsufficientNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(),
            NullLogger<BuyerPaymentInsufficientNotificationConsumer>.Instance);

        await sut.Handle(
            new BuyerPaymentInsufficientEvent(
                EventId: Guid.NewGuid(),
                TransactionId: Guid.NewGuid(),
                BuyerId: Guid.NewGuid(),
                RefundTransactionId: Guid.NewGuid(),
                ExpectedAmount: 10.760000m,
                ReceivedAmount: 10.759m,
                Stablecoin: StablecoinType.USDT,
                SourceAddress: "TBuyerSourceWallet00000000000000000",
                TxHash: "0xunder",
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(NotificationType.INSUFFICIENT_PAYMENT, request.Type);
        Assert.Equal("10.76", request.Parameters["ExpectedAmount"]);
        Assert.Equal("10.759", request.Parameters["ReceivedAmount"]);
    }

    [Fact]
    public async Task Excess_ShowsExpectedTwoDecimal_And_SubCentExcessUnrounded()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new BuyerPaymentExcessRefundedNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(),
            NullLogger<BuyerPaymentExcessRefundedNotificationConsumer>.Instance);

        await sut.Handle(
            new BuyerPaymentExcessRefundedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: Guid.NewGuid(),
                BuyerId: Guid.NewGuid(),
                RefundTransactionId: Guid.NewGuid(),
                ExpectedAmount: 10.76m,
                ReceivedAmount: 30.761m,
                ExcessAmount: 20.001m,
                Stablecoin: StablecoinType.USDT,
                SourceAddress: "TBuyerSourceWallet00000000000000000",
                TxHash: "0xover",
                IsMultiPayment: false,
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal("10.76", request.Parameters["ExpectedAmount"]);
        Assert.Equal("30.761", request.Parameters["ReceivedAmount"]);
        Assert.Equal("20.001", request.Parameters["ExcessAmount"]);
    }

    [Fact]
    public void PaymentWindowText_Renders_TheExactPayable()
    {
        // The "Send X USDT" text the buyer reads: X is the 2-decimal payable,
        // the same string the detail screen shows (07 §7.5 payment.expectedAmount).
        var services = new ServiceCollection();
        services.AddLocalization();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();
        var resolver = new ResxNotificationTemplateResolver(
            provider.GetRequiredService<IStringLocalizer<NotificationTemplates>>(),
            NullLogger<ResxNotificationTemplateResolver>.Instance);

        var rendered = resolver.Resolve(
            NotificationType.PAYMENT_WINDOW_OPEN,
            "en",
            new Dictionary<string, string>
            {
                ["Amount"] = MoneyFormat.Display(10.760000m),
                ["PaymentAddress"] = "TDeposit",
            });

        Assert.Contains("Send 10.76 USDT to TDeposit", rendered.Body, StringComparison.Ordinal);
    }
}
