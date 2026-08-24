using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Notifications.Tests.TestSupport;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Notifications.Tests.Integration;

/// <summary>
/// Integration coverage for the WP19 happy-path notification consumers that
/// re-query the transaction/user for recipient + parameters
/// (<see cref="BuyerAcceptedNotificationConsumer"/>,
/// <see cref="PaymentReceivedNotificationConsumer"/>,
/// <see cref="HappyPathMilestoneNotificationConsumer"/>,
/// <see cref="PayoutCompletedNotificationConsumer"/>). A
/// <see cref="RecordingNotificationDispatcher"/> captures the emitted
/// <see cref="Skinora.Notifications.Application.Notifications.NotificationRequest"/>s
/// while the consumer reads against a real SQL Server, so recipient/type/params
/// are asserted directly (template rendering is covered by
/// <see cref="NotificationDispatcherTests"/>).
/// </summary>
public class HappyPathNotificationConsumerTests : IntegrationTestBase
{
    static HappyPathNotificationConsumerTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        NotificationsModuleDbRegistration.RegisterNotificationsModule();
    }

    private const string PaymentAddressValue = "TPaymentAddr0000000000000000000001";

    private User _seller = null!;
    private User _buyer = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000301",
            SteamDisplayName = "SellerTester",
            PreferredLanguage = "en",
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000302",
            SteamDisplayName = "BuyerTester",
            PreferredLanguage = "en",
        };
        context.Set<User>().AddRange(_seller, _buyer);
        await context.SaveChangesAsync();
    }

    private async Task<Transaction> SeedTransactionAsync(
        TransactionStatus status,
        bool withBuyer = true,
        bool withPaymentAddress = false)
    {
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerId = withBuyer ? _buyer.Id : null,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = _buyer.SteamId,
            ItemAssetId = "12345678901",
            ItemClassId = "98765432101",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100.000000m,
            CommissionRate = 0.0200m,
            CommissionAmount = 2.000000m,
            TotalAmount = 102.000000m,
            SellerPayoutAddress = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL",
            PaymentTimeoutMinutes = 60,
        };
        Context.Set<Transaction>().Add(tx);

        if (withPaymentAddress)
        {
            Context.Set<PaymentAddress>().Add(new PaymentAddress
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                Address = PaymentAddressValue,
                HdWalletIndex = 1,
                ExpectedAmount = 102.000000m,
                ExpectedToken = StablecoinType.USDT,
                MonitoringStatus = MonitoringStatus.ACTIVE,
            });
        }

        await Context.SaveChangesAsync();
        return tx;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BuyerAccepted_NotifiesSeller_WithBuyerName()
    {
        var tx = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new BuyerAcceptedNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(), Context,
            NullLogger<BuyerAcceptedNotificationConsumer>.Instance);

        await sut.Handle(
            new BuyerAcceptedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: tx.Id,
                SellerId: _seller.Id,
                BuyerId: _buyer.Id,
                ItemName: tx.ItemName,
                AcceptedAt: DateTime.UtcNow,
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(_seller.Id, request.UserId);
        Assert.Equal(NotificationType.BUYER_ACCEPTED, request.Type);
        Assert.Equal(tx.Id, request.TransactionId);
        Assert.Equal("BuyerTester", request.Parameters["BuyerName"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PaymentReceived_NotifiesSeller_WithAmount()
    {
        var tx = await SeedTransactionAsync(TransactionStatus.PAYMENT_RECEIVED);
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new PaymentReceivedNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(), Context,
            NullLogger<PaymentReceivedNotificationConsumer>.Instance);

        await sut.Handle(
            new PaymentReceivedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: tx.Id,
                Amount: 102m,
                Stablecoin: StablecoinType.USDT,
                TxHash: "0xpaymentconfirmed",
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(_seller.Id, request.UserId);
        Assert.Equal(NotificationType.PAYMENT_RECEIVED, request.Type);
        Assert.Equal(tx.Id, request.TransactionId);
        Assert.Equal("102", request.Parameters["Amount"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StatusChanged_SellerConfirmed_NotifiesBuyer_WithAmountAndAddress()
    {
        var tx = await SeedTransactionAsync(
            TransactionStatus.SELLER_CONFIRMED, withPaymentAddress: true);
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new HappyPathMilestoneNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(), Context,
            NullLogger<HappyPathMilestoneNotificationConsumer>.Instance);

        await sut.Handle(
            new TransactionStatusChangedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: tx.Id,
                FromStatus: TransactionStatus.ACCEPTED,
                ToStatus: TransactionStatus.SELLER_CONFIRMED,
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(_buyer.Id, request.UserId);
        Assert.Equal(NotificationType.PAYMENT_WINDOW_OPEN, request.Type);
        Assert.Equal(tx.Id, request.TransactionId);
        Assert.Equal("102", request.Parameters["Amount"]);
        Assert.Equal(PaymentAddressValue, request.Parameters["PaymentAddress"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StatusChanged_PaymentReceived_NotifiesSeller_NoParams()
    {
        // v3.0 — this leg changed sides. The platform no longer sends the buyer
        // a trade offer; the SELLER is told to deliver the item directly.
        var tx = await SeedTransactionAsync(TransactionStatus.PAYMENT_RECEIVED);
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new HappyPathMilestoneNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(), Context,
            NullLogger<HappyPathMilestoneNotificationConsumer>.Instance);

        await sut.Handle(
            new TransactionStatusChangedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: tx.Id,
                FromStatus: TransactionStatus.SELLER_CONFIRMED,
                ToStatus: TransactionStatus.PAYMENT_RECEIVED,
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        // WP4 (T140-TestClaimsExceedMeasurement) — the recipient SET is the
        // claim, so it is asserted as a set.
        //
        // This used to read Single() + Equal(seller) + DoesNotContain(buyer).
        // The last assertion could never fail on its own: Single() had already
        // made a second recipient impossible, so the "buyer is excluded" claim
        // was dead weight and a mutation flipping the sides died two lines
        // earlier. Comparing the whole set keeps both halves load-bearing —
        // a recipient added, or the side swapped, fails right here.
        //
        // 03 §3.5 step 3 gives the buyer a realtime status update and no inbox
        // row for this transition; both of the transition's notifications are
        // defined to the seller in the 06 §2.13 catalogue. This is the leg that
        // flipped sides in v3.0, i.e. the one a future edit is likeliest to
        // flip back.
        Assert.Equal(new[] { _seller.Id }, dispatcher.Requests.Select(r => r.UserId));

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(NotificationType.DELIVERY_EXPECTED, request.Type);
        Assert.Equal(tx.Id, request.TransactionId);
        Assert.Empty(request.Parameters);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StatusChanged_UnrelatedTransition_EmitsNothing()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new HappyPathMilestoneNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(), Context,
            NullLogger<HappyPathMilestoneNotificationConsumer>.Instance);

        // The acceptance leg (CREATED → ACCEPTED) is not one of the two statuses
        // this consumer covers; it returns before any DB read.
        await sut.Handle(
            new TransactionStatusChangedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: Guid.NewGuid(),
                FromStatus: TransactionStatus.CREATED,
                ToStatus: TransactionStatus.ACCEPTED,
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PayoutCompleted_NotifiesSellerTwice_AndBuyerOnce()
    {
        var tx = await SeedTransactionAsync(TransactionStatus.COMPLETED);
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new PayoutCompletedNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(), Context,
            NullLogger<PayoutCompletedNotificationConsumer>.Instance);

        await sut.Handle(
            new PayoutCompletedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: tx.Id,
                PayoutTxHash: "0xpayout",
                NetAmount: 99.7m,
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        Assert.Equal(3, dispatcher.Requests.Count);

        var sellerPayout = Assert.Single(
            dispatcher.Requests,
            r => r.Type == NotificationType.SELLER_PAYMENT_SENT);
        Assert.Equal(_seller.Id, sellerPayout.UserId);
        Assert.Equal("99.7", sellerPayout.Parameters["Amount"]);

        var completed = dispatcher.Requests
            .Where(r => r.Type == NotificationType.TRANSACTION_COMPLETED)
            .ToList();
        Assert.Equal(2, completed.Count);
        Assert.Contains(completed, r => r.UserId == _seller.Id);
        Assert.Contains(completed, r => r.UserId == _buyer.Id);
        Assert.All(completed, r => Assert.Equal("AK-47 | Redline", r.Parameters["ItemName"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PayoutCompleted_OpenLinkNoBuyer_NotifiesSellerOnly()
    {
        var tx = await SeedTransactionAsync(TransactionStatus.COMPLETED, withBuyer: false);
        var dispatcher = new RecordingNotificationDispatcher();
        var sut = new PayoutCompletedNotificationConsumer(
            dispatcher, new InMemoryProcessedEventStore(), Context,
            NullLogger<PayoutCompletedNotificationConsumer>.Instance);

        await sut.Handle(
            new PayoutCompletedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: tx.Id,
                PayoutTxHash: "0xpayout",
                NetAmount: 99.7m,
                OccurredAt: DateTime.UtcNow),
            CancellationToken.None);

        Assert.Equal(2, dispatcher.Requests.Count);
        Assert.All(dispatcher.Requests, r => Assert.Equal(_seller.Id, r.UserId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PayoutCompleted_Idempotent_WhenEventAlreadyProcessed()
    {
        var tx = await SeedTransactionAsync(TransactionStatus.COMPLETED);
        var dispatcher = new RecordingNotificationDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new PayoutCompletedNotificationConsumer(
            dispatcher, processed, Context,
            NullLogger<PayoutCompletedNotificationConsumer>.Instance);

        var domainEvent = new PayoutCompletedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: tx.Id,
            PayoutTxHash: "0xpayout",
            NetAmount: 99.7m,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Equal(3, dispatcher.Requests.Count);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Equal(3, dispatcher.Requests.Count);
    }
}
