using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Tests.Integration.Timeouts;

/// <summary>
/// Unit-level coverage for <see cref="TimeoutSideEffectPublisher"/> (T49 —
/// 02 §3.2, 03 §4.1–§4.4). Asserts the phase → event mapping for every
/// <see cref="TimeoutPhase"/> using an in-memory
/// <see cref="CapturingOutboxService"/>; no DB roundtrip needed.
/// </summary>
public class TimeoutSideEffectPublisherTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly CapturingOutboxService _outbox = new();

    private TimeoutSideEffectPublisher CreateSut() =>
        new(_outbox, _clock, NullLogger<TimeoutSideEffectPublisher>.Instance);

    private static Transaction NewTransaction(
        TransactionStatus statusAfterFlip,
        Guid? buyerId = null,
        string? buyerRefundAddress = null) => new()
        {
            Id = Guid.NewGuid(),
            Status = statusAfterFlip,
            SellerId = Guid.NewGuid(),
            BuyerId = buyerId,
            BuyerRefundAddress = buyerRefundAddress,
            BuyerIdentificationMethod = BuyerIdentificationMethod.OPEN_LINK,
            ItemAssetId = "100200300",
            ItemClassId = "abc",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = TimeoutTestFixtures.ValidWallet,
            PaymentTimeoutMinutes = 1440,
        };

    [Fact]
    public async Task Accept_Phase_Emits_Only_Notification_Event()
    {
        var tx = NewTransaction(TransactionStatus.CANCELLED_TIMEOUT);
        await CreateSut().PublishAsync(tx, TransactionStatus.CREATED);

        var evt = Assert.IsType<TransactionTimedOutEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(TimeoutPhase.Accept, evt.Phase);
        Assert.Equal(tx.Id, evt.TransactionId);
        Assert.Equal(tx.SellerId, evt.SellerId);
        Assert.Null(evt.BuyerId);
        Assert.Equal(tx.ItemName, evt.ItemName);
    }

    [Fact]
    public async Task Accept_Phase_Carries_Buyer_When_Registered()
    {
        var buyerId = Guid.NewGuid();
        var tx = NewTransaction(TransactionStatus.CANCELLED_TIMEOUT, buyerId, TimeoutTestFixtures.ValidWallet);

        await CreateSut().PublishAsync(tx, TransactionStatus.CREATED);

        var evt = Assert.IsType<TransactionTimedOutEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(buyerId, evt.BuyerId);
    }

    [Fact]
    public async Task TradeOfferToSeller_Phase_Emits_Only_Notification_Event()
    {
        var tx = NewTransaction(TransactionStatus.CANCELLED_TIMEOUT,
            Guid.NewGuid(), TimeoutTestFixtures.ValidWallet);

        await CreateSut().PublishAsync(tx, TransactionStatus.TRADE_OFFER_SENT_TO_SELLER);

        var evt = Assert.IsType<TransactionTimedOutEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(TimeoutPhase.TradeOfferToSeller, evt.Phase);
    }

    [Fact]
    public async Task Payment_Phase_Emits_Notification_ItemRefund_And_LatePaymentMonitor()
    {
        var buyerId = Guid.NewGuid();
        var refundAddress = TimeoutTestFixtures.ValidWallet;
        var tx = NewTransaction(TransactionStatus.CANCELLED_TIMEOUT, buyerId, refundAddress);

        await CreateSut().PublishAsync(tx, TransactionStatus.ITEM_ESCROWED);

        Assert.Equal(3, _outbox.Published.Count);

        var notify = Assert.Single(_outbox.Published.OfType<TransactionTimedOutEvent>());
        Assert.Equal(TimeoutPhase.Payment, notify.Phase);

        var itemRefund = Assert.Single(_outbox.Published.OfType<ItemRefundToSellerRequestedEvent>());
        Assert.Equal(tx.Id, itemRefund.TransactionId);
        Assert.Equal(tx.SellerId, itemRefund.SellerId);
        Assert.Equal(TimeoutPhase.Payment, itemRefund.Trigger);

        var monitor = Assert.Single(_outbox.Published.OfType<LatePaymentMonitorRequestedEvent>());
        Assert.Equal(tx.Id, monitor.TransactionId);
        Assert.Equal(buyerId, monitor.BuyerId);
        Assert.Equal(refundAddress, monitor.BuyerRefundAddress);
    }

    [Fact]
    public async Task Payment_Phase_Skips_LatePaymentMonitor_When_Buyer_Missing()
    {
        // Buyer should always be present at ITEM_ESCROWED per 06 §3.5, but the
        // publisher must not throw if a schema regression leaves the field null.
        var tx = NewTransaction(TransactionStatus.CANCELLED_TIMEOUT, buyerId: null, buyerRefundAddress: null);

        await CreateSut().PublishAsync(tx, TransactionStatus.ITEM_ESCROWED);

        Assert.Empty(_outbox.Published.OfType<LatePaymentMonitorRequestedEvent>());
        Assert.Single(_outbox.Published.OfType<ItemRefundToSellerRequestedEvent>());
        Assert.Single(_outbox.Published.OfType<TransactionTimedOutEvent>());
    }

    [Fact]
    public async Task Delivery_Phase_Emits_Notification_ItemRefund_And_PaymentRefund()
    {
        var buyerId = Guid.NewGuid();
        var refundAddress = TimeoutTestFixtures.ValidWallet;
        var tx = NewTransaction(TransactionStatus.CANCELLED_TIMEOUT, buyerId, refundAddress);

        await CreateSut().PublishAsync(tx, TransactionStatus.TRADE_OFFER_SENT_TO_BUYER);

        Assert.Equal(3, _outbox.Published.Count);

        var notify = Assert.Single(_outbox.Published.OfType<TransactionTimedOutEvent>());
        Assert.Equal(TimeoutPhase.Delivery, notify.Phase);

        var itemRefund = Assert.Single(_outbox.Published.OfType<ItemRefundToSellerRequestedEvent>());
        Assert.Equal(TimeoutPhase.Delivery, itemRefund.Trigger);

        var paymentRefund = Assert.Single(_outbox.Published.OfType<PaymentRefundToBuyerRequestedEvent>());
        Assert.Equal(tx.Id, paymentRefund.TransactionId);
        Assert.Equal(buyerId, paymentRefund.BuyerId);
        Assert.Equal(refundAddress, paymentRefund.BuyerRefundAddress);
    }

    [Fact]
    public async Task Unsupported_PreviousStatus_Throws()
    {
        var tx = NewTransaction(TransactionStatus.CANCELLED_TIMEOUT);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateSut().PublishAsync(tx, TransactionStatus.PAYMENT_RECEIVED));
    }
}
