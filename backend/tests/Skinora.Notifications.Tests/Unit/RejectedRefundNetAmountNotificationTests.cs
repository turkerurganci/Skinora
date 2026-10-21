using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Application.Notifications;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Resources;
using Skinora.Notifications.Tests.TestSupport;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// T159a — owner decision 2026-10-20 (02 §4.7, 08 §3.4): a rejected-payment
/// refund (underpayment, overpayment excess, multi-payment, wrong token, late
/// payment) is sent net of its own network fee. The buyer's notification must
/// say what actually arrives — the net amount and the deducted fee — in every
/// locale; a pre-T159a event (no RefundAmount) still renders, as a gross
/// refund with a zero fee.
/// </summary>
[Trait("Category", "Unit")]
public class RejectedRefundNetAmountNotificationTests
{
    public static TheoryData<string> Locales() => ["en", "tr", "es", "zh"];

    [Theory]
    [MemberData(nameof(Locales))]
    public async Task Insufficient_Shows_The_Net_Refund_And_The_Fee(string locale)
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var request = await HandleAsync(dispatcher, new BuyerPaymentInsufficientNotificationConsumer(
                dispatcher, new InMemoryProcessedEventStore(),
                NullLogger<BuyerPaymentInsufficientNotificationConsumer>.Instance),
            new BuyerPaymentInsufficientEvent(
                EventId: Guid.NewGuid(), TransactionId: Guid.NewGuid(), BuyerId: Guid.NewGuid(),
                RefundTransactionId: Guid.NewGuid(), ExpectedAmount: 100m, ReceivedAmount: 50m,
                Stablecoin: StablecoinType.USDT, SourceAddress: "TSource", TxHash: "0xu",
                OccurredAt: DateTime.UtcNow, RefundAddress: "TRefund",
                RefundAmount: 48m, GasFee: 2m));

        Assert.Equal("48.00", request.Parameters["RefundAmount"]);
        Assert.Equal("2.00", request.Parameters["GasFee"]);
        AssertRendersNetAndFee(request, locale, "48.00", "2.00");
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public async Task Excess_Shows_The_Net_Refund_And_The_Fee(string locale)
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var request = await HandleAsync(dispatcher, new BuyerPaymentExcessRefundedNotificationConsumer(
                dispatcher, new InMemoryProcessedEventStore(),
                NullLogger<BuyerPaymentExcessRefundedNotificationConsumer>.Instance),
            new BuyerPaymentExcessRefundedEvent(
                EventId: Guid.NewGuid(), TransactionId: Guid.NewGuid(), BuyerId: Guid.NewGuid(),
                RefundTransactionId: Guid.NewGuid(), ExpectedAmount: 100m, ReceivedAmount: 110m,
                ExcessAmount: 10m, Stablecoin: StablecoinType.USDT, SourceAddress: "TSource",
                TxHash: "0xo", IsMultiPayment: false, OccurredAt: DateTime.UtcNow,
                RefundAddress: "0x1111111111111111111111111111111111111111",
                RefundAmount: 9.95m, GasFee: 0.05m));

        Assert.Equal("9.95", request.Parameters["RefundAmount"]);
        Assert.Equal("0.05", request.Parameters["GasFee"]);
        AssertRendersNetAndFee(request, locale, "9.95", "0.05");
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public async Task WrongToken_Shows_The_Net_Refund_And_The_Fee(string locale)
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var request = await HandleAsync(dispatcher, new WrongTokenRefundRequestedNotificationConsumer(
                dispatcher, new InMemoryProcessedEventStore(),
                NullLogger<WrongTokenRefundRequestedNotificationConsumer>.Instance),
            new WrongTokenRefundRequestedEvent(
                EventId: Guid.NewGuid(), TransactionId: Guid.NewGuid(), BuyerId: Guid.NewGuid(),
                RefundTransactionId: Guid.NewGuid(), ExpectedStablecoin: StablecoinType.USDT,
                ActualStablecoin: StablecoinType.USDC, ActualContractAddress: "TUsdc",
                ReceivedAmount: 50m, SourceAddress: "TSource", TxHash: "0xw",
                OccurredAt: DateTime.UtcNow, RefundAddress: "TRefund",
                RefundAmount: 48m, GasFee: 2m));

        AssertRendersNetAndFee(request, locale, "48.00", "2.00");
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public async Task LatePayment_Amount_Is_The_Net_Refund(string locale)
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var request = await HandleAsync(dispatcher, new LatePaymentRefundRequestedNotificationConsumer(
                dispatcher, new InMemoryProcessedEventStore(),
                NullLogger<LatePaymentRefundRequestedNotificationConsumer>.Instance),
            new LatePaymentRefundRequestedEvent(
                EventId: Guid.NewGuid(), TransactionId: Guid.NewGuid(), BuyerId: Guid.NewGuid(),
                RefundTransactionId: Guid.NewGuid(), ReceivedAmount: 100m,
                Stablecoin: StablecoinType.USDT, SourceAddress: "TSource", TxHash: "0xl",
                MonitorState: MonitoringStatus.POST_CANCEL_24H, OccurredAt: DateTime.UtcNow,
                RefundAddress: "TRefund", RefundAmount: 98m, GasFee: 2m));

        // {Amount} is what arrives — not the 100 that was sent.
        Assert.Equal("98.00", request.Parameters["Amount"]);
        AssertRendersNetAndFee(request, locale, "98.00", "2.00");
    }

    [Fact]
    public async Task PreT159a_Event_Without_RefundAmount_Renders_As_A_Gross_Refund()
    {
        // An outbox row written before T159a: its refund was queued gross.
        var dispatcher = new RecordingNotificationDispatcher();
        var request = await HandleAsync(dispatcher, new BuyerPaymentExcessRefundedNotificationConsumer(
                dispatcher, new InMemoryProcessedEventStore(),
                NullLogger<BuyerPaymentExcessRefundedNotificationConsumer>.Instance),
            new BuyerPaymentExcessRefundedEvent(
                EventId: Guid.NewGuid(), TransactionId: Guid.NewGuid(), BuyerId: Guid.NewGuid(),
                RefundTransactionId: Guid.NewGuid(), ExpectedAmount: 100m, ReceivedAmount: 110m,
                ExcessAmount: 10m, Stablecoin: StablecoinType.USDT, SourceAddress: "TSource",
                TxHash: "0xo", IsMultiPayment: false, OccurredAt: DateTime.UtcNow,
                RefundAddress: "TRefund"));

        Assert.Equal("10.00", request.Parameters["RefundAmount"]);
        Assert.Equal("0.00", request.Parameters["GasFee"]);
    }

    private static async Task<NotificationRequest> HandleAsync<TEvent>(
        RecordingNotificationDispatcher dispatcher,
        NotificationConsumerBase<TEvent> consumer,
        TEvent domainEvent)
        where TEvent : Skinora.Shared.Domain.IDomainEvent
    {
        await consumer.Handle(domainEvent, CancellationToken.None);
        return Assert.Single(dispatcher.Requests);
    }

    private static void AssertRendersNetAndFee(
        NotificationRequest request, string locale, string net, string fee)
    {
        var services = new ServiceCollection();
        services.AddLocalization();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();
        var resolver = new ResxNotificationTemplateResolver(
            provider.GetRequiredService<IStringLocalizer<NotificationTemplates>>(),
            NullLogger<ResxNotificationTemplateResolver>.Instance);

        var rendered = resolver.Resolve(request.Type, locale, request.Parameters);

        Assert.Contains(net, rendered.Body, StringComparison.Ordinal);
        Assert.Contains(fee, rendered.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("{", rendered.Body, StringComparison.Ordinal);
    }
}
