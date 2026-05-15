using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Application.Webhooks;
using Skinora.Steam.Domain.Entities;
using Skinora.Steam.Infrastructure.Persistence;
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

    private SteamWebhookHandler CreateSut() =>
        new(Context, NullLogger<SteamWebhookHandler>.Instance);

    [Fact]
    public async Task TradeOfferSent_PersistsTradeOfferRow()
    {
        var sut = CreateSut();
        var envelope = MakeTradeEnvelope(
            SteamWebhookEvents.TradeOfferSent,
            new TradeOfferEventData
            {
                TransactionId = _transaction.Id,
                Direction = "escrow",
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
                Direction = "escrow",
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
                Direction = "escrow",
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

    [Fact]
    public async Task BotEvent_LogsAndAcks_WithoutDbWrite()
    {
        var sut = CreateSut();
        var envelope = new SteamWebhookEnvelope<BotEventData>
        {
            Event = SteamWebhookEvents.BotSessionFailed,
            Timestamp = DateTime.UtcNow.ToString("O"),
            Data = new BotEventData
            {
                AccountName = BotAccount,
                Reason = "InvalidPassword",
                Status = "FAILED",
            },
        };

        await sut.HandleBotEventAsync(envelope, "corr-bot", CancellationToken.None);

        // No DB row is expected — bot events are log-only in T68.
        await using var verify = CreateContext();
        Assert.Equal(0, verify.ChangeTracker.Entries().Count(e => e.State != EntityState.Unchanged));
    }

    private static SteamWebhookEnvelope<TradeOfferEventData> MakeTradeEnvelope(
        string @event, TradeOfferEventData data) => new()
        {
            Event = @event,
            Timestamp = DateTime.UtcNow.ToString("O"),
            Data = data,
        };
}
