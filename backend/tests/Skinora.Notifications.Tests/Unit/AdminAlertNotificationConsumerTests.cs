using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// WP8 — unit coverage for the admin-alert notification consumers: the
/// broadcast fan-out to every admin (FLAG_ALERT / ESCALATION / PAYMENT_FAILURE
/// / STEAM_BOT_ISSUE), the single-admin payout-issue escalation, the
/// no-admins no-op and the inherited consumer-idempotency contract.
/// </summary>
public class AdminAlertNotificationConsumerTests
{
    private static readonly Guid Admin1 = Guid.NewGuid();
    private static readonly Guid Admin2 = Guid.NewGuid();

    [Fact]
    [Trait("Category", "Unit")]
    public async Task FraudFlagCreated_FansOutAdminFlagAlert_ToEveryAdmin_WithFlagId()
    {
        var dispatcher = new RecordingDispatcher();
        var sut = new FraudFlagCreatedAdminNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(), TwoAdmins(),
            NullLogger<FraudFlagCreatedAdminNotificationConsumer>.Instance);

        var flagId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var domainEvent = new FraudFlagCreatedEvent(
            EventId: Guid.NewGuid(),
            FraudFlagId: flagId,
            UserId: Guid.NewGuid(),
            TransactionId: transactionId,
            Scope: FraudFlagScope.TRANSACTION_PRE_CREATE,
            Type: FraudFlagType.PRICE_DEVIATION,
            EmergencyHoldAppliedToActiveTransactions: false,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Equal(2, dispatcher.Requests.Count);
        Assert.Contains(dispatcher.Requests, r => r.UserId == Admin1);
        Assert.Contains(dispatcher.Requests, r => r.UserId == Admin2);
        Assert.All(dispatcher.Requests, r =>
        {
            Assert.Equal(NotificationType.ADMIN_FLAG_ALERT, r.Type);
            Assert.Equal(flagId, r.FlagId);
            Assert.Equal(transactionId, r.TransactionId);
            Assert.Equal(transactionId.ToString("D"), r.Parameters["TransactionId"]);
            Assert.Equal("PRICE_DEVIATION", r.Parameters["Reason"]);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task FraudFlagCreated_AccountLevel_HasFlagId_ButNoTransactionTarget()
    {
        var dispatcher = new RecordingDispatcher();
        var sut = new FraudFlagCreatedAdminNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(), TwoAdmins(),
            NullLogger<FraudFlagCreatedAdminNotificationConsumer>.Instance);

        var domainEvent = new FraudFlagCreatedEvent(
            EventId: Guid.NewGuid(),
            FraudFlagId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            TransactionId: null,
            Scope: FraudFlagScope.ACCOUNT_LEVEL,
            Type: FraudFlagType.MULTI_ACCOUNT,
            EmergencyHoldAppliedToActiveTransactions: true,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.All(dispatcher.Requests, r =>
        {
            Assert.Null(r.TransactionId);
            Assert.NotNull(r.FlagId);
            Assert.Equal("(account-level)", r.Parameters["TransactionId"]);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DisputeEscalated_FansOutAdminEscalation_ToEveryAdmin()
    {
        var dispatcher = new RecordingDispatcher();
        var sut = new DisputeEscalatedAdminNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(), TwoAdmins(),
            NullLogger<DisputeEscalatedAdminNotificationConsumer>.Instance);

        var transactionId = Guid.NewGuid();
        var domainEvent = new DisputeEscalatedEvent(
            EventId: Guid.NewGuid(),
            DisputeId: Guid.NewGuid(),
            TransactionId: transactionId,
            Type: DisputeType.WRONG_ITEM,
            SellerId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            AutoEscalated: true,
            Detail: null,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Equal(2, dispatcher.Requests.Count);
        Assert.All(dispatcher.Requests, r =>
        {
            Assert.Equal(NotificationType.ADMIN_ESCALATION, r.Type);
            Assert.Equal(transactionId, r.TransactionId);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RefundBlocked_FansOutPaymentFailure_WithReasonErrorCode()
    {
        var dispatcher = new RecordingDispatcher();
        var sut = new RefundBlockedAdminNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(), TwoAdmins(),
            NullLogger<RefundBlockedAdminNotificationConsumer>.Instance);

        var transactionId = Guid.NewGuid();
        var domainEvent = new RefundBlockedAdminAlertEvent(
            EventId: Guid.NewGuid(),
            TransactionId: transactionId,
            Reason: RefundBlockedReason.BelowMinimumThreshold,
            TotalPaid: 1.5m,
            GasFee: 2.0m,
            NetRefund: -0.5m,
            MinimumThreshold: 1.0m,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Equal(2, dispatcher.Requests.Count);
        Assert.All(dispatcher.Requests, r =>
        {
            Assert.Equal(NotificationType.ADMIN_PAYMENT_FAILURE, r.Type);
            Assert.Equal(transactionId, r.TransactionId);
            Assert.Contains("REFUND_BLOCKED", r.Parameters["ErrorCode"]);
            Assert.Contains("BelowMinimumThreshold", r.Parameters["ErrorCode"]);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TransferDispatchFailed_FansOutPaymentFailure_WithTypeAndErrorCode()
    {
        var dispatcher = new RecordingDispatcher();
        var sut = new TransferDispatchFailedAdminNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(), TwoAdmins(),
            NullLogger<TransferDispatchFailedAdminNotificationConsumer>.Instance);

        var transactionId = Guid.NewGuid();
        var domainEvent = new TransferDispatchFailedEvent(
            EventId: Guid.NewGuid(),
            BlockchainTransactionId: Guid.NewGuid(),
            TransactionId: transactionId,
            Type: BlockchainTransactionType.SELLER_PAYOUT,
            Token: StablecoinType.USDT,
            Amount: 97.0m,
            ToAddress: "TKnEzG4qX5n6ZRSeller7B9C2D3E4F5G6H7",
            LastErrorCode: "TRANSFER_BROADCAST_REJECTED",
            LastErrorMessage: "node rejected",
            RetryCount: 3,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Equal(2, dispatcher.Requests.Count);
        Assert.All(dispatcher.Requests, r =>
        {
            Assert.Equal(NotificationType.ADMIN_PAYMENT_FAILURE, r.Type);
            Assert.Equal(transactionId, r.TransactionId);
            Assert.Contains("SELLER_PAYOUT", r.Parameters["ErrorCode"]);
            Assert.Contains("TRANSFER_BROADCAST_REJECTED", r.Parameters["ErrorCode"]);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TransferDispatchFailed_NullLastErrorCode_FallsBackToDefaultCode()
    {
        var dispatcher = new RecordingDispatcher();
        var sut = new TransferDispatchFailedAdminNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(), TwoAdmins(),
            NullLogger<TransferDispatchFailedAdminNotificationConsumer>.Instance);

        var transactionId = Guid.NewGuid();
        var domainEvent = new TransferDispatchFailedEvent(
            EventId: Guid.NewGuid(),
            BlockchainTransactionId: Guid.NewGuid(),
            TransactionId: transactionId,
            Type: BlockchainTransactionType.BUYER_REFUND,
            Token: StablecoinType.USDC,
            Amount: 50.0m,
            ToAddress: "TKnEzG4qX5n6ZRBuyer7B9C2D3E4F5G6H7",
            // Sidecar gave no error code on the final attempt — the consumer must
            // substitute the TRANSFER_DISPATCH_FAILED sentinel rather than emit
            // "BUYER_REFUND:" with a dangling separator.
            LastErrorCode: null,
            LastErrorMessage: null,
            RetryCount: 3,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Equal(2, dispatcher.Requests.Count);
        Assert.All(dispatcher.Requests, r =>
        {
            Assert.Equal(NotificationType.ADMIN_PAYMENT_FAILURE, r.Type);
            Assert.Equal(transactionId, r.TransactionId);
            Assert.Equal("BUYER_REFUND:TRANSFER_DISPATCH_FAILED", r.Parameters["ErrorCode"]);
        });
    }

    // v3.0 — BotSessionFailed_FansOutSteamBotIssue_WithBotAndIssue removed with
    // the bot custody layer: the platform runs no Steam bots, so there is no
    // BotSessionFailedEvent and no ADMIN_STEAM_BOT_ISSUE notification (02 §15).

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SellerPayoutIssueEscalated_NotifiesAssignedAdminOnly()
    {
        var dispatcher = new RecordingDispatcher();
        var sut = new SellerPayoutIssueEscalatedNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(),
            NullLogger<SellerPayoutIssueEscalatedNotificationConsumer>.Instance);

        var assignedAdmin = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var domainEvent = new SellerPayoutIssueEscalatedEvent(
            EventId: Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            TransactionId: transactionId,
            SellerId: Guid.NewGuid(),
            EscalatedToAdminId: assignedAdmin,
            VerificationMessage: "auto-verify could not confirm",
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(assignedAdmin, request.UserId);
        Assert.Equal(NotificationType.ADMIN_PAYMENT_FAILURE, request.Type);
        Assert.Equal(transactionId, request.TransactionId);
        Assert.Equal("PAYOUT_ESCALATED", request.Parameters["ErrorCode"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BroadcastConsumer_WithNoAdmins_IsNoOp()
    {
        var dispatcher = new RecordingDispatcher();
        var sut = new RefundBlockedAdminNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(),
            new StubAdminRecipientResolver(),
            NullLogger<RefundBlockedAdminNotificationConsumer>.Instance);

        var domainEvent = new RefundBlockedAdminAlertEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            Reason: RefundBlockedReason.NegativeAmount,
            TotalPaid: 0.1m,
            GasFee: 2.0m,
            NetRefund: -1.9m,
            MinimumThreshold: 1.0m,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BroadcastConsumer_Idempotent_OnReplay()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new FraudFlagCreatedAdminNotificationConsumer(
            dispatcher, processed, TwoAdmins(),
            NullLogger<FraudFlagCreatedAdminNotificationConsumer>.Instance);

        var domainEvent = new FraudFlagCreatedEvent(
            EventId: Guid.NewGuid(),
            FraudFlagId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            Scope: FraudFlagScope.TRANSACTION_PRE_CREATE,
            Type: FraudFlagType.HIGH_VOLUME,
            EmergencyHoldAppliedToActiveTransactions: false,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Equal(2, dispatcher.Requests.Count);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Equal(2, dispatcher.Requests.Count);
    }

    private static StubAdminRecipientResolver TwoAdmins() => new(Admin1, Admin2);

    private sealed class StubAdminRecipientResolver : IAdminRecipientResolver
    {
        private readonly IReadOnlyList<Guid> _adminUserIds;

        public StubAdminRecipientResolver(params Guid[] adminUserIds) => _adminUserIds = adminUserIds;

        public Task<IReadOnlyList<Guid>> GetAdminUserIdsAsync(CancellationToken cancellationToken)
            => Task.FromResult(_adminUserIds);
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
