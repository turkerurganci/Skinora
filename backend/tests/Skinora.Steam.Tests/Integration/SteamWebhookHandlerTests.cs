using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Application.Recovery;
using Skinora.Steam.Application.Webhooks;
using Skinora.Steam.Domain.Entities;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Steam.Tests.TestSupport;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Steam.Tests.Integration;

/// <summary>
/// T68 — webhook handler integration tests against a real SQL Server. Exercises
/// the TradeOffer upsert + state machine wiring for the seven sidecar trade
/// events plus the bot lifecycle log path. Each test class gets its own
/// database via <see cref="IntegrationTestBase"/>.
/// </summary>
public class SteamWebhookHandlerTests : IntegrationTestBase
{
    private const string BotAccount = "EscrowBot-T68";

    static SteamWebhookHandlerTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
        // T69 — AuditLogger writes AuditLog rows; entity lives in Skinora.Platform
        // module so the test AppDbContext must include its configuration too.
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private User _seller = null!;
    private User _buyer = null!;
    private PlatformSteamBot _bot = null!;
    private Transaction _transaction = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000000101", SteamDisplayName = "Seller" };
        _buyer = new User { Id = Guid.NewGuid(), SteamId = "76561198000000102", SteamDisplayName = "Buyer" };
        context.Set<User>().AddRange(_seller, _buyer);

        _bot = new PlatformSteamBot
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198099999102",
            DisplayName = BotAccount,
            Status = PlatformSteamBotStatus.ACTIVE,
        };
        context.Set<PlatformSteamBot>().Add(_bot);

        _transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.ACCEPTED,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            EscrowBotId = _bot.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = _buyer.SteamId,
            BuyerRefundAddress = "TKnEzG4qX5n6ZRSeller7B9C2D3E4F5G6H7",
            ItemAssetId = "asset-1",
            ItemClassId = "cls",
            ItemInstanceId = "inst",
            ItemName = "AK-47",
            ItemIconUrl = "https://cdn.test/ak.png",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.03m,
            CommissionAmount = 3m,
            TotalAmount = 103m,
            SellerPayoutAddress = "TKnEzG4qX5n6ZRBuyer7B9C2D3E4F5G6H7",
        };
        context.Set<Transaction>().Add(_transaction);

        await context.SaveChangesAsync();
    }

    private RecordingNotificationRealtimePublisher _recorder = null!;
    private RecordingOutbox _outbox = null!;

    private SteamWebhookHandler CreateSut()
    {
        _recorder = new RecordingNotificationRealtimePublisher();
        _outbox = new RecordingOutbox();
        // Real materialiser so the F3 boundary-race safety net is exercised
        // end-to-end (recovery row + EMERGENCY_HOLD actually persisted). It is a
        // no-op for the ACTIVE-bot tests since AcceptEscrowAsync only invokes it
        // when the receiving bot is RESTRICTED/BANNED.
        var freeze = new TimeoutFreezeService(
            Context, new NoOpJobScheduler(), new NoOpScheduling(), TimeProvider.System);
        var materialiser = new BotRecoveryMaterialiser(
            Context, freeze, _outbox, new AuditLogger(Context, TimeProvider.System), TimeProvider.System);
        return new SteamWebhookHandler(
            Context,
            new AuditLogger(Context, TimeProvider.System),
            _recorder,
            _outbox,
            materialiser,
            TimeProvider.System,
            NullLogger<SteamWebhookHandler>.Instance);
    }

    private sealed class RecordingOutbox : IOutboxService
    {
        public List<IDomainEvent> Events { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpJobScheduler : IBackgroundJobScheduler
    {
        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay) => Guid.NewGuid().ToString("N");
        public string Enqueue<T>(Expression<Action<T>> methodCall) => Guid.NewGuid().ToString("N");
        public bool Delete(string jobId) => true;
        public void AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>> methodCall, string cronExpression) { }
    }

    private sealed class NoOpScheduling : ITimeoutSchedulingService
    {
        public Task<TimeoutJobIds> SchedulePaymentTimeoutAsync(Guid transactionId, CancellationToken cancellationToken)
            => Task.FromResult(new TimeoutJobIds("p", "w"));

        public Task CancelTimeoutJobsAsync(Guid transactionId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<TimeoutJobIds> ReschedulePaymentTimeoutAsync(
            Guid transactionId, TimeSpan remaining, DateTime newPaymentDeadlineUtc, CancellationToken cancellationToken)
            => Task.FromResult(new TimeoutJobIds("p", "w"));
    }

    [Fact]
    public async Task TradeOfferSent_PersistsTradeOfferRow()
    {
        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(
            SteamWebhookEvents.TradeOfferSent,
            new TradeOfferEventData
            {
                TransactionId = _transaction.Id,
                Direction = "SELLER_TO_BOT",
                BotAccountName = BotAccount,
                OfferId = "9001",
                Status = "sent",
                Attempts = 1,
            });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);

        Assert.Equal(TradeWebhookResult.Applied, result);

        await using var verify = CreateContext();
        var row = await verify.Set<TradeOffer>()
            .SingleAsync(t => t.SteamTradeOfferId == "9001");
        Assert.Equal(TradeOfferDirection.TO_SELLER, row.Direction);
        Assert.Equal(TradeOfferStatus.SENT, row.Status);
        Assert.Equal(_transaction.Id, row.TransactionId);
        Assert.Equal(_bot.Id, row.PlatformSteamBotId);
        Assert.NotNull(row.SentAt);
    }

    [Fact]
    public async Task TradeOfferSent_DuplicateOfferId_IsIdempotent()
    {
        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(
            SteamWebhookEvents.TradeOfferSent,
            new TradeOfferEventData
            {
                TransactionId = _transaction.Id,
                Direction = "SELLER_TO_BOT",
                BotAccountName = BotAccount,
                OfferId = "9002",
                Status = "sent",
                Attempts = 1,
            });

        await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);
        var second = await sut.HandleTradeEventAsync(envelope, "corr-2", CancellationToken.None);

        Assert.Equal(TradeWebhookResult.Idempotent, second);

        await using var verify = CreateContext();
        var count = await verify.Set<TradeOffer>().CountAsync(t => t.SteamTradeOfferId == "9002");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task TradeOfferSent_UnknownTransaction_AcksWithoutInsert()
    {
        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(
            SteamWebhookEvents.TradeOfferSent,
            new TradeOfferEventData
            {
                TransactionId = Guid.NewGuid(),
                Direction = "SELLER_TO_BOT",
                BotAccountName = BotAccount,
                OfferId = "9003",
                Status = "sent",
                Attempts = 1,
            });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);

        Assert.Equal(TradeWebhookResult.Unknown, result);
        await using var verify = CreateContext();
        Assert.False(await verify.Set<TradeOffer>().AnyAsync(t => t.SteamTradeOfferId == "9003"));
    }

    [Fact]
    public async Task TradeOfferAccepted_OnEscrowDirection_FiresEscrowItemTrigger()
    {
        // Pre-stage: transaction must be at TRADE_OFFER_SENT_TO_SELLER to permit
        // the EscrowItem trigger, and EscrowBotAssetId must be set (HasFieldsForItemEscrowed guard).
        await using (var arrange = CreateContext())
        {
            var arrangeTx = await arrange.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
            arrangeTx.Status = TransactionStatus.TRADE_OFFER_SENT_TO_SELLER;
            arrangeTx.EscrowBotAssetId = "asset-on-bot-1";
            await arrange.SaveChangesAsync();

            arrange.Set<TradeOffer>().Add(new TradeOffer
            {
                Id = Guid.NewGuid(),
                TransactionId = _transaction.Id,
                PlatformSteamBotId = _bot.Id,
                Direction = TradeOfferDirection.TO_SELLER,
                SteamTradeOfferId = "9100",
                Status = TradeOfferStatus.SENT,
                SentAt = DateTime.UtcNow,
            });
            await arrange.SaveChangesAsync();
        }

        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(
            SteamWebhookEvents.TradeOfferAccepted,
            new TradeOfferEventData
            {
                Direction = "escrow",
                OfferId = "9100",
                BotAccountName = BotAccount,
                NewState = 3,
                OldState = 2,
            });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);

        Assert.Equal(TradeWebhookResult.Applied, result);

        await using var verify = CreateContext();
        var row = await verify.Set<TradeOffer>().SingleAsync(t => t.SteamTradeOfferId == "9100");
        Assert.Equal(TradeOfferStatus.ACCEPTED, row.Status);
        Assert.NotNull(row.RespondedAt);

        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, tx.Status);
        Assert.NotNull(tx.ItemEscrowedAt);

        // WP9 — RT1 TransactionStatusChanged push staged on the same SaveChanges.
        var statusEvent = Assert.Single(_outbox.Events.OfType<TransactionStatusChangedEvent>());
        Assert.Equal(_transaction.Id, statusEvent.TransactionId);
        Assert.Equal(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, statusEvent.FromStatus);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, statusEvent.ToStatus);
    }

    [Fact]
    public async Task TradeOfferDeclined_OnEscrowDirection_FiresSellerDeclineTrigger()
    {
        await using (var arrange = CreateContext())
        {
            var arrangeTx = await arrange.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
            arrangeTx.Status = TransactionStatus.TRADE_OFFER_SENT_TO_SELLER;
            await arrange.SaveChangesAsync();

            arrange.Set<TradeOffer>().Add(new TradeOffer
            {
                Id = Guid.NewGuid(),
                TransactionId = _transaction.Id,
                PlatformSteamBotId = _bot.Id,
                Direction = TradeOfferDirection.TO_SELLER,
                SteamTradeOfferId = "9200",
                Status = TradeOfferStatus.SENT,
                SentAt = DateTime.UtcNow,
            });
            await arrange.SaveChangesAsync();
        }

        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(
            SteamWebhookEvents.TradeOfferDeclined,
            new TradeOfferEventData
            {
                Direction = "escrow",
                OfferId = "9200",
                NewState = 7,
                OldState = 2,
            });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);

        Assert.Equal(TradeWebhookResult.Applied, result);

        await using var verify = CreateContext();
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_SELLER, tx.Status);
        Assert.Equal(CancelledByType.SELLER, tx.CancelledBy);
        Assert.False(string.IsNullOrEmpty(tx.CancelReason));
    }

    [Fact]
    public async Task TradeOfferExpired_FiresTimeoutTriggerWithCancelReason()
    {
        await using (var arrange = CreateContext())
        {
            var arrangeTx = await arrange.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
            arrangeTx.Status = TransactionStatus.TRADE_OFFER_SENT_TO_SELLER;
            await arrange.SaveChangesAsync();

            arrange.Set<TradeOffer>().Add(new TradeOffer
            {
                Id = Guid.NewGuid(),
                TransactionId = _transaction.Id,
                PlatformSteamBotId = _bot.Id,
                Direction = TradeOfferDirection.TO_SELLER,
                SteamTradeOfferId = "9300",
                Status = TradeOfferStatus.SENT,
                SentAt = DateTime.UtcNow,
            });
            await arrange.SaveChangesAsync();
        }

        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(
            SteamWebhookEvents.TradeOfferExpired,
            new TradeOfferEventData
            {
                Direction = "escrow",
                OfferId = "9300",
                NewState = 5,
                OldState = 2,
            });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);

        Assert.Equal(TradeWebhookResult.Applied, result);

        await using var verify = CreateContext();
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, tx.Status);
    }

    [Fact]
    public async Task TradeOfferCountered_IsTreatedAsCancellation()
    {
        await using (var arrange = CreateContext())
        {
            var arrangeTx = await arrange.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
            arrangeTx.Status = TransactionStatus.TRADE_OFFER_SENT_TO_SELLER;
            await arrange.SaveChangesAsync();

            arrange.Set<TradeOffer>().Add(new TradeOffer
            {
                Id = Guid.NewGuid(),
                TransactionId = _transaction.Id,
                PlatformSteamBotId = _bot.Id,
                Direction = TradeOfferDirection.TO_SELLER,
                SteamTradeOfferId = "9400",
                Status = TradeOfferStatus.SENT,
                SentAt = DateTime.UtcNow,
            });
            await arrange.SaveChangesAsync();
        }

        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(
            SteamWebhookEvents.TradeOfferCountered,
            new TradeOfferEventData
            {
                Direction = "escrow",
                OfferId = "9400",
                NewState = 4,
                OldState = 2,
            });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);

        Assert.Equal(TradeWebhookResult.Applied, result);

        await using var verify = CreateContext();
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_SELLER, tx.Status);
        var offer = await verify.Set<TradeOffer>().SingleAsync(t => t.SteamTradeOfferId == "9400");
        Assert.Equal(TradeOfferStatus.DECLINED, offer.Status);
    }

    [Fact]
    public async Task StatusChange_UnknownOfferId_AckedAsUnknown()
    {
        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(
            SteamWebhookEvents.TradeOfferAccepted,
            new TradeOfferEventData
            {
                Direction = "escrow",
                OfferId = "does-not-exist",
                NewState = 3,
                OldState = 2,
            });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);
        Assert.Equal(TradeWebhookResult.Unknown, result);
    }

    [Fact]
    public async Task StatusChange_SameStatusReplay_IsIdempotent()
    {
        await using (var arrange = CreateContext())
        {
            arrange.Set<TradeOffer>().Add(new TradeOffer
            {
                Id = Guid.NewGuid(),
                TransactionId = _transaction.Id,
                PlatformSteamBotId = _bot.Id,
                Direction = TradeOfferDirection.TO_SELLER,
                SteamTradeOfferId = "9500",
                Status = TradeOfferStatus.ACCEPTED,
                SentAt = DateTime.UtcNow.AddMinutes(-5),
                RespondedAt = DateTime.UtcNow,
            });
            await arrange.SaveChangesAsync();
        }

        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(
            SteamWebhookEvents.TradeOfferAccepted,
            new TradeOfferEventData
            {
                Direction = "escrow",
                OfferId = "9500",
                NewState = 3,
                OldState = 2,
            });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);
        Assert.Equal(TradeWebhookResult.Idempotent, result);
    }

    // ---------- T106a — asset-id capture + escrow-count lifecycle ----------

    [Fact]
    public async Task TradeOfferAccepted_Escrow_SetsAssetIdFromPayloadAndIncrementsCount()
    {
        await StageOfferAsync(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, TradeOfferDirection.TO_SELLER, "A100");

        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(SteamWebhookEvents.TradeOfferAccepted, new TradeOfferEventData
        {
            OfferId = "A100",
            BotAccountName = BotAccount,
            NewState = 3,
            OldState = 2,
            ReceivedAssetId = "bot-asset-x",
        });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);

        Assert.Equal(TradeWebhookResult.Applied, result);
        await using var verify = CreateContext();
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, tx.Status);
        Assert.Equal("bot-asset-x", tx.EscrowBotAssetId);
        var bot = await verify.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
        Assert.Equal(1, bot.ActiveEscrowCount);
    }

    [Fact]
    public async Task TradeOfferAccepted_Escrow_OnRestrictedBot_OpensRecoveryAndHolds()
    {
        // T103b-2 F3 boundary race — the seller accepted while the bot was
        // already RESTRICTED, so the item lands on a degraded bot the recovery
        // sweep had already snapshotted past. The inline safety net must open the
        // recovery queue + auto-hold in the same unit of work.
        await StageOfferAsync(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, TradeOfferDirection.TO_SELLER, "A200");
        await using (var arrange = CreateContext())
        {
            var arrangeBot = await arrange.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
            arrangeBot.Status = PlatformSteamBotStatus.RESTRICTED;
            arrangeBot.RestrictionReason = "restricted";
            await arrange.SaveChangesAsync();
        }

        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(SteamWebhookEvents.TradeOfferAccepted, new TradeOfferEventData
        {
            OfferId = "A200",
            BotAccountName = BotAccount,
            NewState = 3,
            OldState = 2,
            ReceivedAssetId = "bot-asset-restricted",
        });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-race", CancellationToken.None);

        Assert.Equal(TradeWebhookResult.Applied, result);
        await using var verify = CreateContext();

        // The item still advanced to ITEM_ESCROWED (it physically reached the bot)…
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, tx.Status);
        Assert.Equal("bot-asset-restricted", tx.EscrowBotAssetId);
        // …but is now auto-held so its timeout clock is frozen.
        Assert.True(tx.IsOnHold);
        Assert.Equal(TimeoutFreezeReason.EMERGENCY_HOLD, tx.TimeoutFreezeReason);

        // A PENDING recovery row was materialised for the stuck transaction.
        var recovery = Assert.Single(
            await verify.Set<BotRecoveryItem>().Where(r => r.PlatformSteamBotId == _bot.Id).ToListAsync());
        Assert.Equal(_transaction.Id, recovery.TransactionId);
        Assert.Equal(BotRecoveryStatus.PENDING, recovery.RecoveryStatus);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, recovery.StatusAtRestriction);

        // Auto-hold raised its notification event.
        Assert.Single(_outbox.Events.OfType<EmergencyHoldAppliedEvent>());
    }

    [Fact]
    public async Task TradeOfferAccepted_Escrow_OnActiveBot_DoesNotOpenRecovery()
    {
        // Control for the F3 test: the common ACTIVE-bot accept must NOT touch the
        // recovery queue or hold the transaction.
        await StageOfferAsync(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, TradeOfferDirection.TO_SELLER, "A201");

        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(SteamWebhookEvents.TradeOfferAccepted, new TradeOfferEventData
        {
            OfferId = "A201",
            BotAccountName = BotAccount,
            NewState = 3,
            OldState = 2,
            ReceivedAssetId = "bot-asset-active",
        });

        await sut.HandleTradeEventAsync(envelope, "corr-active", CancellationToken.None);

        await using var verify = CreateContext();
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, tx.Status);
        Assert.False(tx.IsOnHold);
        Assert.Empty(await verify.Set<BotRecoveryItem>().ToListAsync());
        Assert.Empty(_outbox.Events.OfType<EmergencyHoldAppliedEvent>());
    }

    [Fact]
    public async Task TradeOfferAccepted_Escrow_MissingAssetId_DoesNotAdvance()
    {
        await StageOfferAsync(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, TradeOfferDirection.TO_SELLER, "A101");

        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(SteamWebhookEvents.TradeOfferAccepted, new TradeOfferEventData
        {
            OfferId = "A101",
            BotAccountName = BotAccount,
            NewState = 3,
            OldState = 2,
            // No ReceivedAssetId — the sidecar exchange-details fetch failed.
        });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);

        Assert.Equal(TradeWebhookResult.Idempotent, result);
        await using var verify = CreateContext();
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
        Assert.Equal(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, tx.Status);
        Assert.Null(tx.EscrowBotAssetId);
        var offer = await verify.Set<TradeOffer>().SingleAsync(o => o.SteamTradeOfferId == "A101");
        Assert.Equal(TradeOfferStatus.ACCEPTED, offer.Status);
        var bot = await verify.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
        Assert.Equal(0, bot.ActiveEscrowCount);
    }

    [Fact]
    public async Task TradeOfferAccepted_Delivery_SetsDeliveredAssetIdAndDecrementsCount()
    {
        await using (var arrange = CreateContext())
        {
            var arrangeTx = await arrange.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
            arrangeTx.Status = TransactionStatus.TRADE_OFFER_SENT_TO_BUYER;
            arrangeTx.EscrowBotAssetId = "bot-asset-x"; // set when the item was escrowed
            var arrangeBot = await arrange.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
            arrangeBot.ActiveEscrowCount = 1;
            arrange.Set<TradeOffer>().Add(new TradeOffer
            {
                Id = Guid.NewGuid(),
                TransactionId = _transaction.Id,
                PlatformSteamBotId = _bot.Id,
                Direction = TradeOfferDirection.TO_BUYER,
                SteamTradeOfferId = "A102",
                Status = TradeOfferStatus.SENT,
                SentAt = DateTime.UtcNow,
            });
            await arrange.SaveChangesAsync();
        }

        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(SteamWebhookEvents.TradeOfferAccepted, new TradeOfferEventData
        {
            OfferId = "A102",
            BotAccountName = BotAccount,
            NewState = 3,
            OldState = 2,
            DeliveredAssetId = "buyer-asset-y",
        });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);

        Assert.Equal(TradeWebhookResult.Applied, result);
        await using var verify = CreateContext();
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, tx.Status);
        Assert.Equal("buyer-asset-y", tx.DeliveredBuyerAssetId);
        var bot = await verify.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
        Assert.Equal(0, bot.ActiveEscrowCount);

        // WP9 — RT1 TransactionStatusChanged push staged on the same SaveChanges.
        var statusEvent = Assert.Single(_outbox.Events.OfType<TransactionStatusChangedEvent>());
        Assert.Equal(_transaction.Id, statusEvent.TransactionId);
        Assert.Equal(TransactionStatus.TRADE_OFFER_SENT_TO_BUYER, statusEvent.FromStatus);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, statusEvent.ToStatus);
    }

    [Fact]
    public async Task TradeOfferAccepted_Refund_DecrementsCountWithoutTrigger()
    {
        await using (var arrange = CreateContext())
        {
            var arrangeTx = await arrange.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
            arrangeTx.Status = TransactionStatus.CANCELLED_TIMEOUT;
            arrangeTx.CancelledBy = CancelledByType.TIMEOUT;
            arrangeTx.CancelReason = "Payment timeout";
            arrangeTx.CancelledAt = DateTime.UtcNow;
            var arrangeBot = await arrange.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
            arrangeBot.ActiveEscrowCount = 1;
            arrange.Set<TradeOffer>().Add(new TradeOffer
            {
                Id = Guid.NewGuid(),
                TransactionId = _transaction.Id,
                PlatformSteamBotId = _bot.Id,
                Direction = TradeOfferDirection.RETURN_TO_SELLER,
                SteamTradeOfferId = "A103",
                Status = TradeOfferStatus.SENT,
                SentAt = DateTime.UtcNow,
            });
            await arrange.SaveChangesAsync();
        }

        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(SteamWebhookEvents.TradeOfferAccepted, new TradeOfferEventData
        {
            OfferId = "A103",
            BotAccountName = BotAccount,
            NewState = 3,
            OldState = 2,
        });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);

        Assert.Equal(TradeWebhookResult.Applied, result);
        await using var verify = CreateContext();
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, tx.Status); // unchanged — terminal
        var offer = await verify.Set<TradeOffer>().SingleAsync(o => o.SteamTradeOfferId == "A103");
        Assert.Equal(TradeOfferStatus.ACCEPTED, offer.Status);
        var bot = await verify.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
        Assert.Equal(0, bot.ActiveEscrowCount);
    }

    [Fact]
    public async Task TradeOfferFailed_DuplicateForDirection_IsIdempotent()
    {
        await using (var arrange = CreateContext())
        {
            arrange.Set<TradeOffer>().Add(new TradeOffer
            {
                Id = Guid.NewGuid(),
                TransactionId = _transaction.Id,
                PlatformSteamBotId = _bot.Id,
                Direction = TradeOfferDirection.TO_SELLER,
                SteamTradeOfferId = null,
                Status = TradeOfferStatus.FAILED,
                RetryCount = 3,
            });
            await arrange.SaveChangesAsync();
        }

        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(SteamWebhookEvents.TradeOfferFailed, new TradeOfferEventData
        {
            TransactionId = _transaction.Id,
            Direction = "SELLER_TO_BOT",
            BotAccountName = BotAccount,
            Reason = "permanent",
            Attempts = 3,
        });

        var result = await sut.HandleTradeEventAsync(envelope, "corr-1", CancellationToken.None);

        Assert.Equal(TradeWebhookResult.Idempotent, result);
        await using var verify = CreateContext();
        var count = await verify.Set<TradeOffer>()
            .CountAsync(o => o.TransactionId == _transaction.Id && o.Direction == TradeOfferDirection.TO_SELLER);
        Assert.Equal(1, count);
    }

    /// <summary>Stage a transaction in <paramref name="status"/> with a SENT offer of the given direction.</summary>
    private async Task StageOfferAsync(TransactionStatus status, TradeOfferDirection direction, string offerId)
    {
        await using var arrange = CreateContext();
        var tx = await arrange.Set<Transaction>().SingleAsync(t => t.Id == _transaction.Id);
        tx.Status = status;
        arrange.Set<TradeOffer>().Add(new TradeOffer
        {
            Id = Guid.NewGuid(),
            TransactionId = _transaction.Id,
            PlatformSteamBotId = _bot.Id,
            Direction = direction,
            SteamTradeOfferId = offerId,
            Status = TradeOfferStatus.SENT,
            SentAt = DateTime.UtcNow,
        });
        await arrange.SaveChangesAsync();
    }

    [Fact]
    public async Task BotEvent_Restricted_UpdatesStatusAuditsAndPushes()
    {
        var sut = CreateSut();
        var envelope = MakeBotEnvelope(
            SteamWebhookEvents.BotRemovedFromPool,
            reason: "restricted",
            status: "FAILED");

        await sut.HandleBotEventAsync(envelope, "corr-restricted", CancellationToken.None);

        await using var verify = CreateContext();
        var refreshed = await verify.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
        Assert.Equal(PlatformSteamBotStatus.RESTRICTED, refreshed.Status);
        Assert.NotNull(refreshed.LastHealthCheckAt);
        // T103b-2 — the sidecar reason is surfaced for the admin S18 card.
        Assert.Equal("restricted", refreshed.RestrictionReason);

        var auditRows = await verify.Set<AuditLog>()
            .Where(a => a.EntityType == nameof(PlatformSteamBot) && a.EntityId == _bot.Id.ToString())
            .ToListAsync();
        // WP8 — two rows now: the terse BOT_STATUS_CHANGED transition plus the
        // dedicated BOT_SESSION_FAILED incident record (JSON envelope).
        var statusAudit = auditRows.Single(a => a.Action == AuditAction.BOT_STATUS_CHANGED);
        Assert.Equal(ActorType.SYSTEM, statusAudit.ActorType);
        Assert.Equal(PlatformSteamBotStatus.ACTIVE.ToString(), statusAudit.OldValue);
        Assert.Contains("RESTRICTED", statusAudit.NewValue);
        Assert.Contains("reason=restricted", statusAudit.NewValue);

        var incidentAudit = auditRows.Single(a => a.Action == AuditAction.BOT_SESSION_FAILED);
        Assert.Equal(ActorType.SYSTEM, incidentAudit.ActorType);
        Assert.Equal(PlatformSteamBotStatus.ACTIVE.ToString(), incidentAudit.OldValue);
        Assert.Contains("RESTRICTED", incidentAudit.NewValue);
        Assert.Contains("restricted", incidentAudit.NewValue);
        Assert.Contains(SteamWebhookEvents.BotRemovedFromPool, incidentAudit.NewValue);

        var push = Assert.Single(_recorder.Calls);
        Assert.Equal("AdminBotStatusChanged", push.Method);
        var payload = Assert.IsType<NotificationRealtimePayloads.AdminBotStatusChanged>(push.Payload);
        Assert.Equal(_bot.Id, payload.BotId);
        Assert.Equal(PlatformSteamBotStatus.ACTIVE.ToString(), payload.PreviousStatus);
        Assert.Equal(PlatformSteamBotStatus.RESTRICTED.ToString(), payload.NewStatus);
        Assert.Equal("restricted", payload.Reason);

        // T103b-2 — a restriction raises BotRestrictedEvent for the recovery consumer.
        var restricted = Assert.Single(_outbox.Events.OfType<BotRestrictedEvent>());
        Assert.Equal(_bot.Id, restricted.PlatformSteamBotId);
        Assert.Equal(PlatformSteamBotStatus.RESTRICTED.ToString(), restricted.Status);
        Assert.Equal("restricted", restricted.Reason);

        // WP8 — and a BotSessionFailedEvent for the ADMIN_STEAM_BOT_ISSUE alert.
        var incident = Assert.Single(_outbox.Events.OfType<BotSessionFailedEvent>());
        Assert.Equal(_bot.Id, incident.PlatformSteamBotId);
        Assert.Equal(PlatformSteamBotStatus.ACTIVE.ToString(), incident.PreviousStatus);
        Assert.Equal(PlatformSteamBotStatus.RESTRICTED.ToString(), incident.NewStatus);
        Assert.Equal("restricted", incident.Reason);
        Assert.Equal(SteamWebhookEvents.BotRemovedFromPool, incident.WebhookEvent);
    }

    [Fact]
    public async Task BotEvent_Banned_UpdatesStatusToBanned()
    {
        var sut = CreateSut();
        var envelope = MakeBotEnvelope(
            SteamWebhookEvents.BotSessionFailed,
            reason: "banned",
            status: "BANNED");

        await sut.HandleBotEventAsync(envelope, "corr-banned", CancellationToken.None);

        await using var verify = CreateContext();
        var refreshed = await verify.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
        Assert.Equal(PlatformSteamBotStatus.BANNED, refreshed.Status);

        var payload = Assert.IsType<NotificationRealtimePayloads.AdminBotStatusChanged>(_recorder.Calls.Single().Payload);
        Assert.Equal(PlatformSteamBotStatus.BANNED.ToString(), payload.NewStatus);
        Assert.Equal("banned", payload.Reason);

        // T103b-2 — a ban also raises BotRestrictedEvent.
        var restricted = Assert.Single(_outbox.Events.OfType<BotRestrictedEvent>());
        Assert.Equal(PlatformSteamBotStatus.BANNED.ToString(), restricted.Status);
        // WP8 — and the BotSessionFailedEvent admin alert.
        Assert.Single(_outbox.Events.OfType<BotSessionFailedEvent>());
    }

    [Fact]
    public async Task BotEvent_SessionRecoveryFailed_SetsOffline()
    {
        var sut = CreateSut();
        var envelope = MakeBotEnvelope(
            SteamWebhookEvents.BotRemovedFromPool,
            reason: "session_recovery_failed",
            status: "FAILED");

        await sut.HandleBotEventAsync(envelope, "corr-offline", CancellationToken.None);

        await using var verify = CreateContext();
        var refreshed = await verify.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
        Assert.Equal(PlatformSteamBotStatus.OFFLINE, refreshed.Status);
        // T103b-2 — OFFLINE is treated as transient: no recovery event is raised
        // (new traffic is already diverted by the ACTIVE-only selector).
        Assert.Empty(_outbox.Events.OfType<BotRestrictedEvent>());
        // WP8 — but the lifecycle incident still raises the admin alert event.
        var incident = Assert.Single(_outbox.Events.OfType<BotSessionFailedEvent>());
        Assert.Equal(PlatformSteamBotStatus.OFFLINE.ToString(), incident.NewStatus);
    }

    [Fact]
    public async Task BotEvent_IdempotentSameStatus_DoesNotAuditOrPush()
    {
        // Pre-set the bot to RESTRICTED so a second "restricted" event collides.
        await using (var arrange = CreateContext())
        {
            var b = await arrange.Set<PlatformSteamBot>().SingleAsync(x => x.Id == _bot.Id);
            b.Status = PlatformSteamBotStatus.RESTRICTED;
            await arrange.SaveChangesAsync();
        }

        var sut = CreateSut();
        var envelope = MakeBotEnvelope(
            SteamWebhookEvents.BotRemovedFromPool,
            reason: "restricted",
            status: "FAILED");

        await sut.HandleBotEventAsync(envelope, "corr-idem", CancellationToken.None);

        await using var verify = CreateContext();
        var refreshed = await verify.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
        Assert.Equal(PlatformSteamBotStatus.RESTRICTED, refreshed.Status);
        Assert.NotNull(refreshed.LastHealthCheckAt); // probe observation kept

        // No new audit row, no SignalR push, no recovery event (idempotent ack).
        Assert.False(await verify.Set<AuditLog>()
            .AnyAsync(a => a.Action == AuditAction.BOT_STATUS_CHANGED));
        Assert.Empty(_recorder.Calls);
        Assert.Empty(_outbox.Events);
    }

    [Fact]
    public async Task BotEvent_UnknownAccount_LogsAndAcksWithoutStateChange()
    {
        var sut = CreateSut();
        var envelope = MakeBotEnvelope(
            SteamWebhookEvents.BotSessionFailed,
            reason: "restricted",
            status: "FAILED",
            accountName: "GhostBot-Unknown");

        await sut.HandleBotEventAsync(envelope, "corr-unknown", CancellationToken.None);

        await using var verify = CreateContext();
        var refreshed = await verify.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
        Assert.Equal(PlatformSteamBotStatus.ACTIVE, refreshed.Status);
        Assert.False(await verify.Set<AuditLog>().AnyAsync(a => a.Action == AuditAction.BOT_STATUS_CHANGED));
        Assert.Empty(_recorder.Calls);
    }

    [Fact]
    public async Task BotEvent_MissingAccountName_LogsAndAcks()
    {
        var sut = CreateSut();
        // Direct envelope construction — the helper applies a `?? BotAccount`
        // fallback so it cannot exercise the truly-null code path.
        var envelope = new SteamWebhookEnvelope<BotEventData>
        {
            Event = SteamWebhookEvents.BotSessionFailed,
            Timestamp = DateTime.UtcNow.ToString("O"),
            Data = new BotEventData
            {
                AccountName = null,
                Reason = "restricted",
                Status = "FAILED",
            },
        };

        await sut.HandleBotEventAsync(envelope, "corr-missing", CancellationToken.None);

        Assert.Empty(_recorder.Calls);

        await using var verify = CreateContext();
        // No state mutation: the seeded bot stays ACTIVE and no audit row is added.
        var refreshed = await verify.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
        Assert.Equal(PlatformSteamBotStatus.ACTIVE, refreshed.Status);
        Assert.False(await verify.Set<AuditLog>().AnyAsync(a => a.Action == AuditAction.BOT_STATUS_CHANGED));
    }

    private SteamWebhookEnvelope<BotEventData> MakeBotEnvelope(
        string @event,
        string reason,
        string status,
        string? accountName = null) => new()
        {
            Event = @event,
            Timestamp = DateTime.UtcNow.ToString("O"),
            Data = new BotEventData
            {
                AccountName = accountName ?? BotAccount,
                Reason = reason,
                Status = status,
            },
        };

    private static SteamWebhookEnvelope<TradeOfferEventData> MakeTradeEnvelope(
        string @event, TradeOfferEventData data) => new()
        {
            Event = @event,
            Timestamp = DateTime.UtcNow.ToString("O"),
            Data = data,
        };
}
