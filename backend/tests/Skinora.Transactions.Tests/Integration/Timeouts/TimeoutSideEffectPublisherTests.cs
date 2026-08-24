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
            ItemAssetId = Guid.NewGuid().ToString("N")[..12],
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
    public async Task SellerConfirm_Phase_Emits_Only_Notification_Event()
    {
        var tx = NewTransaction(TransactionStatus.CANCELLED_TIMEOUT,
            Guid.NewGuid(), TimeoutTestFixtures.ValidWallet);

        await CreateSut().PublishAsync(tx, TransactionStatus.ACCEPTED);

        var evt = Assert.IsType<TransactionTimedOutEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(TimeoutPhase.SellerConfirm, evt.Phase);
    }

    [Fact]
    public async Task Payment_Phase_Emits_Notification_And_LatePaymentMonitor()
    {
        // v3.0 — no item refund: the item never left the seller's inventory
        // (03 §4.3), so only the notification and the late-payment watch remain.
        var buyerId = Guid.NewGuid();
        var refundAddress = TimeoutTestFixtures.ValidWallet;
        var tx = NewTransaction(TransactionStatus.CANCELLED_TIMEOUT, buyerId, refundAddress);

        await CreateSut().PublishAsync(tx, TransactionStatus.SELLER_CONFIRMED);

        // WP7 (F7Gate-EventsWithoutConsumer) — the payment phase publishes
        // ONE event. LatePaymentMonitorRequestedEvent used to ride along and
        // was removed: nothing consumed it, and the late-payment watch is
        // armed by PostCancelMonitorStarter instead. Asserting the exact
        // count is the point — a re-added orphan publish fails here.
        Assert.Single(_outbox.Published);

        var notify = Assert.Single(_outbox.Published.OfType<TransactionTimedOutEvent>());
        Assert.Equal(TimeoutPhase.Payment, notify.Phase);
    }

    [Fact]
    public async Task Payment_Phase_Skips_LatePaymentMonitor_When_Buyer_Missing()
    {
        // Buyer should always be present at SELLER_CONFIRMED per 06 §3.5, but
        // the publisher must not throw if a schema regression leaves it null.
        var tx = NewTransaction(TransactionStatus.CANCELLED_TIMEOUT, buyerId: null, buyerRefundAddress: null);

        await CreateSut().PublishAsync(tx, TransactionStatus.SELLER_CONFIRMED);

        Assert.Single(_outbox.Published.OfType<TransactionTimedOutEvent>());
        Assert.Single(_outbox.Published);
    }

    [Fact]
    public async Task Delivery_Phase_Emits_Notification_And_PaymentRefund()
    {
        // 03 §4.4 — the SELLER failed to deliver, so the buyer's money goes
        // back. There is no item to return.
        var buyerId = Guid.NewGuid();
        var refundAddress = TimeoutTestFixtures.ValidWallet;
        var tx = NewTransaction(TransactionStatus.CANCELLED_TIMEOUT, buyerId, refundAddress);

        await CreateSut().PublishAsync(tx, TransactionStatus.PAYMENT_RECEIVED);

        Assert.Equal(2, _outbox.Published.Count);

        var notify = Assert.Single(_outbox.Published.OfType<TransactionTimedOutEvent>());
        Assert.Equal(TimeoutPhase.Delivery, notify.Phase);

        var paymentRefund = Assert.Single(_outbox.Published.OfType<PaymentRefundToBuyerRequestedEvent>());
        Assert.Equal(tx.Id, paymentRefund.TransactionId);
        Assert.Equal(buyerId, paymentRefund.BuyerId);
        Assert.Equal(refundAddress, paymentRefund.BuyerRefundAddress);
    }

    [Fact]
    public async Task Unsupported_PreviousStatus_Throws()
    {
        var tx = NewTransaction(TransactionStatus.CANCELLED_TIMEOUT);

        // ITEM_DELIVERED has no timeout phase — from there the transaction is
        // driven by settlement, not by a deadline (02 §4.5.1).
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateSut().PublishAsync(tx, TransactionStatus.ITEM_DELIVERED));
    }
}
