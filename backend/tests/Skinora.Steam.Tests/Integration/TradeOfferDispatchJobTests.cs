using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Application.BotSelection;
using Skinora.Steam.Application.Dispatch;
using Skinora.Steam.Domain.Entities;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Steam.Tests.Integration;

/// <summary>
/// T106a — TradeOfferDispatchJob integration tests. A real SQL Server backs the
/// candidate scan + state-machine wiring; the sidecar HTTP port and the outbox
/// are faked so the assertions cover dispatch outcomes (advance / record FAILED
/// / leave-for-retry), the request shape, and idempotency.
/// </summary>
public sealed class TradeOfferDispatchJobTests : IntegrationTestBase
{
    private const string BotAccount = "EscrowBot-T106a";

    static TradeOfferDispatchJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
    }

    private User _seller = null!;
    private User _buyer = null!;
    private PlatformSteamBot _bot = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000000301", SteamDisplayName = "Seller" };
        _buyer = new User { Id = Guid.NewGuid(), SteamId = "76561198000000302", SteamDisplayName = "Buyer" };
        context.Set<User>().AddRange(_seller, _buyer);

        _bot = new PlatformSteamBot
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198099999301",
            DisplayName = BotAccount,
            Status = PlatformSteamBotStatus.ACTIVE,
        };
        context.Set<PlatformSteamBot>().Add(_bot);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Escrow_HappyPath_AssignsBotAndAdvances()
    {
        var txId = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var client = new FakeDispatchClient(new TradeOfferDispatchResult(
            TradeOfferDispatchStatus.Sent, "off-1", false, 1, null));
        var outbox = new RecordingOutbox();
        var sut = CreateSut(client, outbox);

        await sut.ExecuteAsync();

        await using var verify = CreateContext();
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == txId);
        Assert.Equal(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, tx.Status);
        Assert.Equal(_bot.Id, tx.EscrowBotId);

        // WP9 — RT1 TransactionStatusChanged push staged on the same outbox/SaveChanges.
        var statusEvent = Assert.IsType<TransactionStatusChangedEvent>(Assert.Single(outbox.Events));
        Assert.Equal(txId, statusEvent.TransactionId);
        Assert.Equal(TransactionStatus.ACCEPTED, statusEvent.FromStatus);
        Assert.Equal(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, statusEvent.ToStatus);

        var request = Assert.Single(client.Requests);
        Assert.Equal(TradeOfferDispatchDirection.SellerToBot, request.Direction);
        Assert.Equal(_seller.SteamId, request.PartnerSteamId);
        Assert.Equal(BotAccount, request.BotAccountName);
        Assert.Equal("asset-1", request.Items[0].AssetId);
        Assert.Equal(730, request.Items[0].AppId);
        Assert.Equal("2", request.Items[0].ContextId);
    }

    [Fact]
    public async Task Escrow_NoActiveBot_LeavesAcceptedWithoutDispatch()
    {
        var txId = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        await using (var arrange = CreateContext())
        {
            var bot = await arrange.Set<PlatformSteamBot>().SingleAsync(b => b.Id == _bot.Id);
            bot.Status = PlatformSteamBotStatus.OFFLINE;
            await arrange.SaveChangesAsync();
        }
        var client = new FakeDispatchClient(SentResult());
        var sut = CreateSut(client);

        await sut.ExecuteAsync();

        Assert.Empty(client.Requests);
        await using var verify = CreateContext();
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == txId);
        Assert.Equal(TransactionStatus.ACCEPTED, tx.Status);
        Assert.False(await verify.Set<TradeOffer>().AnyAsync(o => o.TransactionId == txId));
    }

    [Fact]
    public async Task Escrow_FailedResponse_RecordsFailedRowAndPublishesEvent()
    {
        var txId = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var client = new FakeDispatchClient(new TradeOfferDispatchResult(
            TradeOfferDispatchStatus.Failed, null, false, 3, "permanent eResult"));
        var outbox = new RecordingOutbox();
        var sut = CreateSut(client, outbox);

        await sut.ExecuteAsync();

        await using var verify = CreateContext();
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == txId);
        Assert.Equal(TransactionStatus.ACCEPTED, tx.Status);

        var offer = await verify.Set<TradeOffer>()
            .SingleAsync(o => o.TransactionId == txId && o.Direction == TradeOfferDirection.TO_SELLER);
        Assert.Equal(TradeOfferStatus.FAILED, offer.Status);

        var evt = Assert.Single(outbox.Events);
        var failed = Assert.IsType<TradeOfferDispatchFailedEvent>(evt);
        Assert.Equal(txId, failed.TransactionId);
        Assert.Equal(TradeOfferDirection.TO_SELLER, failed.Direction);
    }

    [Fact]
    public async Task Escrow_ExistingOffer_DoesNotDispatch()
    {
        var txId = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        await using (var arrange = CreateContext())
        {
            arrange.Set<TradeOffer>().Add(new TradeOffer
            {
                Id = Guid.NewGuid(),
                TransactionId = txId,
                PlatformSteamBotId = _bot.Id,
                Direction = TradeOfferDirection.TO_SELLER,
                SteamTradeOfferId = "existing",
                Status = TradeOfferStatus.SENT,
                SentAt = DateTime.UtcNow,
            });
            await arrange.SaveChangesAsync();
        }
        var client = new FakeDispatchClient(SentResult());
        var sut = CreateSut(client);

        await sut.ExecuteAsync();

        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Escrow_Unavailable_LeavesForRetry()
    {
        var txId = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var client = new FakeDispatchClient(new TradeOfferDispatchResult(
            TradeOfferDispatchStatus.Unavailable, null, true, 0, "SIDECAR_UNREACHABLE"));
        var sut = CreateSut(client);

        await sut.ExecuteAsync();

        await using var verify = CreateContext();
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == txId);
        Assert.Equal(TransactionStatus.ACCEPTED, tx.Status);
        Assert.False(await verify.Set<TradeOffer>().AnyAsync(o => o.TransactionId == txId));
    }

    [Fact]
    public async Task Delivery_HappyPath_ReusesEscrowBotAndAdvances()
    {
        var txId = await SeedTransactionAsync(
            TransactionStatus.PAYMENT_RECEIVED, escrowBotAssetId: "asset-on-bot", escrowBotId: true);
        var client = new FakeDispatchClient(SentResult());
        var outbox = new RecordingOutbox();
        var sut = CreateSut(client, outbox);

        await sut.ExecuteAsync();

        await using var verify = CreateContext();
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == txId);
        Assert.Equal(TransactionStatus.TRADE_OFFER_SENT_TO_BUYER, tx.Status);

        // WP9 — RT1 TransactionStatusChanged push staged on the same outbox/SaveChanges.
        var statusEvent = Assert.IsType<TransactionStatusChangedEvent>(Assert.Single(outbox.Events));
        Assert.Equal(txId, statusEvent.TransactionId);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, statusEvent.FromStatus);
        Assert.Equal(TransactionStatus.TRADE_OFFER_SENT_TO_BUYER, statusEvent.ToStatus);

        var request = Assert.Single(client.Requests);
        Assert.Equal(TradeOfferDispatchDirection.BotToBuyer, request.Direction);
        Assert.Equal(_buyer.SteamId, request.PartnerSteamId);
        Assert.Equal(BotAccount, request.BotAccountName);
        Assert.Equal("asset-on-bot", request.Items[0].AssetId);
    }

    // ---------- helpers ----------

    private static TradeOfferDispatchResult SentResult()
        => new(TradeOfferDispatchStatus.Sent, "off", false, 1, null);

    private async Task<Guid> SeedTransactionAsync(
        TransactionStatus status, string? escrowBotAssetId = null, bool escrowBotId = false)
    {
        var id = Guid.NewGuid();
        await using var arrange = CreateContext();
        arrange.Set<Transaction>().Add(new Transaction
        {
            Id = id,
            Status = status,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            EscrowBotId = escrowBotId ? _bot.Id : null,
            EscrowBotAssetId = escrowBotAssetId,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = _buyer.SteamId,
            BuyerRefundAddress = "TKnEzG4qX5n6ZRSeller7B9C2D3E4F5G6H7",
            ItemAssetId = "asset-1",
            ItemClassId = "cls",
            ItemName = "AK-47",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.03m,
            CommissionAmount = 3m,
            TotalAmount = 103m,
            SellerPayoutAddress = "TKnEzG4qX5n6ZRBuyer7B9C2D3E4F5G6H7",
        });
        await arrange.SaveChangesAsync();
        return id;
    }

    private TradeOfferDispatchJob CreateSut(
        ITradeOfferDispatchClient client, IOutboxService? outbox = null)
        => new(
            Context,
            new SqlBotSelectionService(Context),
            client,
            outbox ?? new RecordingOutbox(),
            TimeProvider.System,
            NullLogger<TradeOfferDispatchJob>.Instance);

    private sealed class FakeDispatchClient : ITradeOfferDispatchClient
    {
        private readonly TradeOfferDispatchResult _result;
        public List<TradeOfferDispatchRequest> Requests { get; } = [];

        public FakeDispatchClient(TradeOfferDispatchResult result) => _result = result;

        public Task<TradeOfferDispatchResult> SendAsync(
            TradeOfferDispatchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }
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
}
