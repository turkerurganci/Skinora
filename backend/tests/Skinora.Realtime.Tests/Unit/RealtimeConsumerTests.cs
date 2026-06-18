using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Realtime.Application.Contracts;
using Skinora.Realtime.Application.EventHandlers;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;

namespace Skinora.Realtime.Tests.Unit;

/// <summary>
/// Per-consumer assertions: each event maps to the correct method on
/// <see cref="ITransactionRealtimePublisher"/> with the expected payload, and
/// the shared <c>RealtimeConsumerBase</c> idempotency contract holds.
/// </summary>
public class RealtimeConsumerTests
{
    [Fact]
    public async Task BuyerAccepted_PushesStatusChanged_Created_To_Accepted()
    {
        var publisher = new RecordingRealtimePublisher();
        var sut = new BuyerAcceptedRealtimeConsumer(
            publisher, new InMemoryProcessedEventStore(),
            NullLogger<BuyerAcceptedRealtimeConsumer>.Instance);

        var occurredAt = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        var ev = new BuyerAcceptedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            ItemName: "AK-47",
            AcceptedAt: occurredAt,
            OccurredAt: occurredAt);

        await sut.Handle(ev, CancellationToken.None);

        var (method, payload) = Assert.Single(publisher.Calls);
        Assert.Equal("StatusChanged", method);
        var status = Assert.IsType<TransactionRealtimePayloads.TransactionStatusChanged>(payload);
        Assert.Equal(ev.TransactionId, status.TransactionId);
        Assert.Equal(TransactionStatus.CREATED, status.FromStatus);
        Assert.Equal(TransactionStatus.ACCEPTED, status.ToStatus);
        Assert.Equal(occurredAt, status.Timestamp);
    }

    [Theory]
    [InlineData(CancelledByType.SELLER, TransactionStatus.CREATED, TransactionStatus.CANCELLED_SELLER)]
    [InlineData(CancelledByType.BUYER, TransactionStatus.ACCEPTED, TransactionStatus.CANCELLED_BUYER)]
    [InlineData(CancelledByType.ADMIN, TransactionStatus.PAYMENT_RECEIVED, TransactionStatus.CANCELLED_ADMIN)]
    [InlineData(CancelledByType.TIMEOUT, TransactionStatus.ITEM_ESCROWED, TransactionStatus.CANCELLED_TIMEOUT)]
    public async Task TransactionCancelled_MapsCancelledByToTerminalStatus(
        CancelledByType cancelledBy,
        TransactionStatus fromStatus,
        TransactionStatus expectedTo)
    {
        var publisher = new RecordingRealtimePublisher();
        var sut = new TransactionCancelledRealtimeConsumer(
            publisher, new InMemoryProcessedEventStore(),
            NullLogger<TransactionCancelledRealtimeConsumer>.Instance);

        var ev = new TransactionCancelledEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            CancelledBy: cancelledBy,
            SellerId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            ItemName: "AWP",
            CancelReason: "test sebebi",
            FromStatus: fromStatus,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(ev, CancellationToken.None);

        var status = Assert.IsType<TransactionRealtimePayloads.TransactionStatusChanged>(
            publisher.Calls.Single().Payload);
        Assert.Equal(fromStatus, status.FromStatus);
        Assert.Equal(expectedTo, status.ToStatus);
    }

    [Fact]
    public async Task TransactionTimedOut_PushesStatusChangedTo_CancelledTimeout()
    {
        var publisher = new RecordingRealtimePublisher();
        var sut = new TransactionTimedOutRealtimeConsumer(
            publisher, new InMemoryProcessedEventStore(),
            NullLogger<TransactionTimedOutRealtimeConsumer>.Instance);

        var ev = new TransactionTimedOutEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            Phase: TimeoutPhase.Payment,
            SellerId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            ItemName: "M9 Bayonet",
            FromStatus: TransactionStatus.ITEM_ESCROWED,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(ev, CancellationToken.None);

        var status = Assert.IsType<TransactionRealtimePayloads.TransactionStatusChanged>(
            publisher.Calls.Single().Payload);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, status.FromStatus);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, status.ToStatus);
    }

    [Fact]
    public async Task PaymentReceived_PushesPaymentConfirmed_Then_StatusChanged()
    {
        var publisher = new RecordingRealtimePublisher();
        var sut = new PaymentReceivedRealtimeConsumer(
            publisher, new InMemoryProcessedEventStore(),
            NullLogger<PaymentReceivedRealtimeConsumer>.Instance);

        var ev = new PaymentReceivedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            Amount: 12.5m,
            Stablecoin: StablecoinType.USDT,
            TxHash: "0xabc",
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(ev, CancellationToken.None);

        Assert.Equal(2, publisher.Calls.Count);
        Assert.Equal("PaymentConfirmed", publisher.Calls[0].Method);
        var confirmed = Assert.IsType<TransactionRealtimePayloads.PaymentConfirmed>(publisher.Calls[0].Payload);
        Assert.Equal(12.5m, confirmed.Amount);
        Assert.Equal("0xabc", confirmed.TxHash);
        Assert.Equal(20, confirmed.Confirmations);

        Assert.Equal("StatusChanged", publisher.Calls[1].Method);
        var status = Assert.IsType<TransactionRealtimePayloads.TransactionStatusChanged>(publisher.Calls[1].Payload);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, status.FromStatus);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, status.ToStatus);
    }

    [Fact]
    public async Task DisputeAutoResolved_PushesClosedDisputeUpdate()
    {
        var publisher = new RecordingRealtimePublisher();
        var sut = new DisputeAutoResolvedRealtimeConsumer(
            publisher, new InMemoryProcessedEventStore(),
            NullLogger<DisputeAutoResolvedRealtimeConsumer>.Instance);

        var ev = new DisputeAutoResolvedEvent(
            EventId: Guid.NewGuid(),
            DisputeId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            Type: DisputeType.PAYMENT,
            BuyerId: Guid.NewGuid(),
            Outcome: "Ödeme zincirde onaylandı",
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(ev, CancellationToken.None);

        var update = Assert.IsType<TransactionRealtimePayloads.DisputeUpdate>(
            publisher.Calls.Single().Payload);
        Assert.Equal(DisputeStatus.CLOSED, update.Status);
        Assert.Equal("Ödeme zincirde onaylandı", update.AutoCheckResult);
    }

    [Theory]
    [InlineData(true, "AUTO_WRONG_ITEM")]
    [InlineData(false, null)]
    public async Task DisputeEscalated_AutoCheckResult_TracksAutoFlag(
        bool autoEscalated, string? expectedAutoCheckResult)
    {
        var publisher = new RecordingRealtimePublisher();
        var sut = new DisputeEscalatedRealtimeConsumer(
            publisher, new InMemoryProcessedEventStore(),
            NullLogger<DisputeEscalatedRealtimeConsumer>.Instance);

        var ev = new DisputeEscalatedEvent(
            EventId: Guid.NewGuid(),
            DisputeId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            Type: DisputeType.WRONG_ITEM,
            SellerId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            AutoEscalated: autoEscalated,
            Detail: autoEscalated ? null : "alıcı detay açıkladı",
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(ev, CancellationToken.None);

        var update = Assert.IsType<TransactionRealtimePayloads.DisputeUpdate>(
            publisher.Calls.Single().Payload);
        Assert.Equal(DisputeStatus.ESCALATED, update.Status);
        Assert.Equal(expectedAutoCheckResult, update.AutoCheckResult);
    }

    [Fact]
    public async Task FraudFlagApproved_TransactionScope_PushesFlagAndStatus()
    {
        var publisher = new RecordingRealtimePublisher();
        var sut = new FraudFlagApprovedRealtimeConsumer(
            publisher, new InMemoryProcessedEventStore(),
            NullLogger<FraudFlagApprovedRealtimeConsumer>.Instance);

        var transactionId = Guid.NewGuid();
        var ev = new FraudFlagApprovedEvent(
            EventId: Guid.NewGuid(),
            FraudFlagId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            TransactionId: transactionId,
            Scope: FraudFlagScope.TRANSACTION_PRE_CREATE,
            Type: FraudFlagType.PRICE_DEVIATION,
            ReviewedByAdminId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(ev, CancellationToken.None);

        Assert.Equal(2, publisher.Calls.Count);
        var flag = Assert.IsType<TransactionRealtimePayloads.FlagResolved>(publisher.Calls[0].Payload);
        Assert.Equal(transactionId, flag.TransactionId);
        Assert.Equal(ReviewStatus.APPROVED, flag.ReviewStatus);
        var status = Assert.IsType<TransactionRealtimePayloads.TransactionStatusChanged>(publisher.Calls[1].Payload);
        Assert.Equal(TransactionStatus.FLAGGED, status.FromStatus);
        Assert.Equal(TransactionStatus.CREATED, status.ToStatus);
    }

    [Fact]
    public async Task FraudFlagApproved_AccountLevel_NoPush()
    {
        var publisher = new RecordingRealtimePublisher();
        var sut = new FraudFlagApprovedRealtimeConsumer(
            publisher, new InMemoryProcessedEventStore(),
            NullLogger<FraudFlagApprovedRealtimeConsumer>.Instance);

        var ev = new FraudFlagApprovedEvent(
            EventId: Guid.NewGuid(),
            FraudFlagId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            TransactionId: null,
            Scope: FraudFlagScope.ACCOUNT_LEVEL,
            Type: FraudFlagType.HIGH_VOLUME,
            ReviewedByAdminId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(ev, CancellationToken.None);

        Assert.Empty(publisher.Calls);
    }

    [Fact]
    public async Task FraudFlagRejected_TransactionScope_PushesFlagAndStatus()
    {
        var publisher = new RecordingRealtimePublisher();
        var sut = new FraudFlagRejectedRealtimeConsumer(
            publisher, new InMemoryProcessedEventStore(),
            NullLogger<FraudFlagRejectedRealtimeConsumer>.Instance);

        var transactionId = Guid.NewGuid();
        var ev = new FraudFlagRejectedEvent(
            EventId: Guid.NewGuid(),
            FraudFlagId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            TransactionId: transactionId,
            Scope: FraudFlagScope.TRANSACTION_PRE_CREATE,
            Type: FraudFlagType.PRICE_DEVIATION,
            ReviewedByAdminId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(ev, CancellationToken.None);

        Assert.Equal(2, publisher.Calls.Count);
        var flag = Assert.IsType<TransactionRealtimePayloads.FlagResolved>(publisher.Calls[0].Payload);
        Assert.Equal(ReviewStatus.REJECTED, flag.ReviewStatus);
        var status = Assert.IsType<TransactionRealtimePayloads.TransactionStatusChanged>(publisher.Calls[1].Payload);
        Assert.Equal(TransactionStatus.FLAGGED, status.FromStatus);
        Assert.Equal(TransactionStatus.CANCELLED_ADMIN, status.ToStatus);
    }

    [Fact]
    public async Task EmergencyHoldApplied_PushesPayloadWithReason()
    {
        var publisher = new RecordingRealtimePublisher();
        var sut = new EmergencyHoldAppliedRealtimeConsumer(
            publisher, new InMemoryProcessedEventStore(),
            NullLogger<EmergencyHoldAppliedRealtimeConsumer>.Instance);

        var ev = new EmergencyHoldAppliedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            ItemName: "Karambit",
            Reason: "Şüpheli aktivite — manual inceleme",
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(ev, CancellationToken.None);

        var payload = Assert.IsType<TransactionRealtimePayloads.EmergencyHoldApplied>(
            publisher.Calls.Single().Payload);
        Assert.Equal(ev.TransactionId, payload.TransactionId);
        Assert.Equal(ev.Reason, payload.Message);
    }

    [Fact]
    public async Task EmergencyHoldReleased_PushesActionAndResumedStatus()
    {
        var publisher = new RecordingRealtimePublisher();
        var sut = new EmergencyHoldReleasedRealtimeConsumer(
            publisher, new InMemoryProcessedEventStore(),
            NullLogger<EmergencyHoldReleasedRealtimeConsumer>.Instance);

        var ev = new EmergencyHoldReleasedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            ItemName: "Bayonet",
            Action: EmergencyHoldReleaseAction.RESUME,
            ResumedStatus: TransactionStatus.ITEM_ESCROWED,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(ev, CancellationToken.None);

        var payload = Assert.IsType<TransactionRealtimePayloads.EmergencyHoldReleased>(
            publisher.Calls.Single().Payload);
        Assert.Equal(EmergencyHoldReleaseAction.RESUME, payload.Action);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, payload.ResumedStatus);
    }

    [Theory]
    [InlineData(TransactionStatus.ACCEPTED, TransactionStatus.TRADE_OFFER_SENT_TO_SELLER)]
    [InlineData(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, TransactionStatus.ITEM_ESCROWED)]
    [InlineData(TransactionStatus.PAYMENT_RECEIVED, TransactionStatus.TRADE_OFFER_SENT_TO_BUYER)]
    [InlineData(TransactionStatus.TRADE_OFFER_SENT_TO_BUYER, TransactionStatus.ITEM_DELIVERED)]
    public async Task TransactionStatusChanged_RelaysFromAndToVerbatim(
        TransactionStatus from, TransactionStatus to)
    {
        // WP9 — the generic Steam-transition event is a pure relay: the producer
        // (dispatch job / webhook handler) captured from/to around the Fire().
        var publisher = new RecordingRealtimePublisher();
        var sut = new TransactionStatusChangedRealtimeConsumer(
            publisher, new InMemoryProcessedEventStore(),
            NullLogger<TransactionStatusChangedRealtimeConsumer>.Instance);

        var occurredAt = new DateTime(2026, 6, 18, 9, 0, 0, DateTimeKind.Utc);
        var ev = new TransactionStatusChangedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            FromStatus: from,
            ToStatus: to,
            OccurredAt: occurredAt);

        await sut.Handle(ev, CancellationToken.None);

        var (method, payload) = Assert.Single(publisher.Calls);
        Assert.Equal("StatusChanged", method);
        var status = Assert.IsType<TransactionRealtimePayloads.TransactionStatusChanged>(payload);
        Assert.Equal(ev.TransactionId, status.TransactionId);
        Assert.Equal(from, status.FromStatus);
        Assert.Equal(to, status.ToStatus);
        Assert.Equal(occurredAt, status.Timestamp);
    }

    [Fact]
    public async Task PayoutCompleted_PushesStatusChanged_ItemDelivered_To_Completed()
    {
        // WP9 — reuses WP1's PayoutCompletedEvent for the Complete → COMPLETED push.
        var publisher = new RecordingRealtimePublisher();
        var sut = new PayoutCompletedRealtimeConsumer(
            publisher, new InMemoryProcessedEventStore(),
            NullLogger<PayoutCompletedRealtimeConsumer>.Instance);

        var occurredAt = new DateTime(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);
        var ev = new PayoutCompletedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            PayoutTxHash: "0xpayout",
            NetAmount: 42.5m,
            OccurredAt: occurredAt);

        await sut.Handle(ev, CancellationToken.None);

        var (method, payload) = Assert.Single(publisher.Calls);
        Assert.Equal("StatusChanged", method);
        var status = Assert.IsType<TransactionRealtimePayloads.TransactionStatusChanged>(payload);
        Assert.Equal(ev.TransactionId, status.TransactionId);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, status.FromStatus);
        Assert.Equal(TransactionStatus.COMPLETED, status.ToStatus);
        Assert.Equal(occurredAt, status.Timestamp);
    }

    [Fact]
    public async Task BuyerAccepted_Idempotent_When_EventAlreadyProcessed()
    {
        // Mirrors NotificationConsumerBase contract: a replayed event must not
        // produce a second push.
        var publisher = new RecordingRealtimePublisher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new BuyerAcceptedRealtimeConsumer(
            publisher, processed,
            NullLogger<BuyerAcceptedRealtimeConsumer>.Instance);

        var ev = new BuyerAcceptedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            ItemName: "M4A1",
            AcceptedAt: DateTime.UtcNow,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(ev, CancellationToken.None);
        Assert.Single(publisher.Calls);

        await sut.Handle(ev, CancellationToken.None);
        Assert.Single(publisher.Calls);

        Assert.True(await processed.ExistsAsync(ev.EventId, "realtime.buyer-accepted"));
    }
}
