using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Tests.TestSupport;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// WP7 (F7Gate-OrphanNotificationTypes) — the producers for three notification
/// types the 06 §2.13 catalogue promised and nothing ever sent.
/// </summary>
/// <remarks>
/// The templates for all three already existed in all four locales; only the
/// producers were missing, which is what made the gap invisible — the
/// catalogue, the enum, the resx files and the email-category map all agreed
/// the notification existed.
/// </remarks>
[Trait("Category", "Unit")]
public class OrphanNotificationProducerTests
{
    private static readonly Guid Seller = Guid.NewGuid();
    private static readonly Guid Buyer = Guid.NewGuid();
    private static readonly Guid Tx = Guid.NewGuid();

    // ---------- TRANSACTION_FLAGGED ----------

    [Fact]
    public async Task TransactionFlagged_Notifies_The_Flagged_Party()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new TransactionFlaggedNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(),
            NullLogger<TransactionFlaggedNotificationConsumer>.Instance);

        await sut.Handle(
            new FraudFlagCreatedEvent(
                EventId: Guid.NewGuid(),
                FraudFlagId: Guid.NewGuid(),
                UserId: Seller,
                TransactionId: Tx,
                Scope: FraudFlagScope.TRANSACTION_PRE_CREATE,
                Type: FraudFlagType.PRICE_DEVIATION,
                EmergencyHoldAppliedToActiveTransactions: false,
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(Seller, request.UserId);
        Assert.Equal(NotificationType.TRANSACTION_FLAGGED, request.Type);
        Assert.Equal(Tx, request.TransactionId);
        // The template renders {TransactionId}; without the parameter the user
        // would receive the literal placeholder.
        Assert.Equal(Tx.ToString("D"), request.Parameters["TransactionId"]);
    }

    [Fact]
    public async Task TransactionFlagged_Says_Nothing_For_An_Account_Level_Flag()
    {
        // An account-scope flag has no transaction, and "your transaction is
        // under review" would name one that does not exist. Those reach the
        // user through the suspension path instead (02 §14.0).
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new TransactionFlaggedNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(),
            NullLogger<TransactionFlaggedNotificationConsumer>.Instance);

        await sut.Handle(
            new FraudFlagCreatedEvent(
                EventId: Guid.NewGuid(),
                FraudFlagId: Guid.NewGuid(),
                UserId: Seller,
                TransactionId: null,
                Scope: FraudFlagScope.ACCOUNT_LEVEL,
                Type: FraudFlagType.MULTI_ACCOUNT,
                EmergencyHoldAppliedToActiveTransactions: false,
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        Assert.Empty(dispatcher.Requests);
    }

    // ---------- FLAG_RESOLVED ----------

    [Fact]
    public async Task FlagResolved_Reports_An_Approved_Review()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new FlagResolvedNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(),
            NullLogger<FlagResolvedNotificationConsumer>.Instance);

        await sut.Handle(
            new FraudFlagApprovedEvent(
                EventId: Guid.NewGuid(),
                FraudFlagId: Guid.NewGuid(),
                UserId: Seller,
                TransactionId: Tx,
                Scope: FraudFlagScope.TRANSACTION_PRE_CREATE,
                Type: FraudFlagType.PRICE_DEVIATION,
                ReviewedByAdminId: Guid.NewGuid(),
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(NotificationType.FLAG_RESOLVED, request.Type);
        Assert.Equal("APPROVED", request.Parameters["Outcome"]);
    }

    [Fact]
    public async Task FlagResolved_Reports_A_Rejected_Review_Too()
    {
        // The catalogue defines ONE type covering "onay veya red", so both
        // outcomes must arrive — and they must be distinguishable, because an
        // approved flag cancels the transaction while a rejected one releases
        // it. Before WP7 the body text described only the rejection.
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new FlagRejectedNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(),
            NullLogger<FlagRejectedNotificationConsumer>.Instance);

        await sut.Handle(
            new FraudFlagRejectedEvent(
                EventId: Guid.NewGuid(),
                FraudFlagId: Guid.NewGuid(),
                UserId: Seller,
                TransactionId: Tx,
                Scope: FraudFlagScope.TRANSACTION_PRE_CREATE,
                Type: FraudFlagType.PRICE_DEVIATION,
                ReviewedByAdminId: Guid.NewGuid(),
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(NotificationType.FLAG_RESOLVED, request.Type);
        Assert.Equal("REJECTED", request.Parameters["Outcome"]);
    }

    [Fact]
    public async Task FlagResolved_Says_Nothing_For_An_Account_Level_Flag()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new FlagResolvedNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(),
            NullLogger<FlagResolvedNotificationConsumer>.Instance);

        await sut.Handle(
            new FraudFlagApprovedEvent(
                EventId: Guid.NewGuid(),
                FraudFlagId: Guid.NewGuid(),
                UserId: Seller,
                TransactionId: null,
                Scope: FraudFlagScope.ACCOUNT_LEVEL,
                Type: FraudFlagType.MULTI_ACCOUNT,
                ReviewedByAdminId: Guid.NewGuid(),
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        Assert.Empty(dispatcher.Requests);
    }

    // ---------- PAYMENT_REFUNDED ----------

    [Fact]
    public async Task PaymentRefunded_Tells_The_Buyer_Where_The_Money_Went()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new PaymentRefundedNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(),
            NullLogger<PaymentRefundedNotificationConsumer>.Instance);

        await sut.Handle(
            new PaymentRefundToBuyerRequestedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: Tx,
                BuyerId: Buyer,
                BuyerRefundAddress: "TRefundAddr000000000000000000000000",
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(Buyer, request.UserId);
        Assert.Equal(NotificationType.PAYMENT_REFUNDED, request.Type);
        Assert.Equal("TRefundAddr000000000000000000000000", request.Parameters["RefundAddress"]);
        // Deliberately NOT an amount: the gas fee comes off at broadcast time
        // (02 §4.6), so a figure quoted here would misstate what arrives.
        Assert.False(request.Parameters.ContainsKey("Amount"));
    }
}
