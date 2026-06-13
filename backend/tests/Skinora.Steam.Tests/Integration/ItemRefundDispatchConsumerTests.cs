using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Application.Dispatch;
using Skinora.Steam.Domain.Entities;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Steam.Tests.Integration;

/// <summary>
/// T106a refund leg — <see cref="ItemRefundDispatchConsumer"/> integration
/// tests. The consumer reads the transaction / seller / bot and dispatches a
/// RETURN_TO_SELLER offer through a faked sidecar port.
/// </summary>
public sealed class ItemRefundDispatchConsumerTests : IntegrationTestBase
{
    private const string BotAccount = "EscrowBot-Refund";

    static ItemRefundDispatchConsumerTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
    }

    private User _seller = null!;
    private PlatformSteamBot _bot = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000000401", SteamDisplayName = "Seller" };
        context.Set<User>().Add(_seller);
        _bot = new PlatformSteamBot
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198099999401",
            DisplayName = BotAccount,
            Status = PlatformSteamBotStatus.ACTIVE,
        };
        context.Set<PlatformSteamBot>().Add(_bot);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task EscrowedTransaction_DispatchesReturnToSeller()
    {
        var txId = await SeedTransactionAsync(escrowed: true);
        var client = new FakeDispatchClient(SentResult());
        var sut = new ItemRefundDispatchConsumer(Context, client, NullLogger<ItemRefundDispatchConsumer>.Instance);

        await sut.Handle(Event(txId), CancellationToken.None);

        var request = Assert.Single(client.Requests);
        Assert.Equal(TradeOfferDispatchDirection.BotToSellerRefund, request.Direction);
        Assert.Equal(_seller.SteamId, request.PartnerSteamId);
        Assert.Equal(BotAccount, request.BotAccountName);
        Assert.Equal("bot-asset-x", request.Items[0].AssetId);
    }

    [Fact]
    public async Task NotEscrowed_NoOp()
    {
        var txId = await SeedTransactionAsync(escrowed: false);
        var client = new FakeDispatchClient(SentResult());
        var sut = new ItemRefundDispatchConsumer(Context, client, NullLogger<ItemRefundDispatchConsumer>.Instance);

        await sut.Handle(Event(txId), CancellationToken.None);

        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task AlreadyDispatched_Idempotent()
    {
        var txId = await SeedTransactionAsync(escrowed: true);
        await using (var arrange = CreateContext())
        {
            arrange.Set<TradeOffer>().Add(new TradeOffer
            {
                Id = Guid.NewGuid(),
                TransactionId = txId,
                PlatformSteamBotId = _bot.Id,
                Direction = TradeOfferDirection.RETURN_TO_SELLER,
                SteamTradeOfferId = "r-1",
                Status = TradeOfferStatus.SENT,
                SentAt = DateTime.UtcNow,
            });
            await arrange.SaveChangesAsync();
        }
        var client = new FakeDispatchClient(SentResult());
        var sut = new ItemRefundDispatchConsumer(Context, client, NullLogger<ItemRefundDispatchConsumer>.Instance);

        await sut.Handle(Event(txId), CancellationToken.None);

        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Unavailable_Throws_ForOutboxRetry()
    {
        var txId = await SeedTransactionAsync(escrowed: true);
        var client = new FakeDispatchClient(new TradeOfferDispatchResult(
            TradeOfferDispatchStatus.Unavailable, null, true, 0, "SIDECAR_UNREACHABLE"));
        var sut = new ItemRefundDispatchConsumer(Context, client, NullLogger<ItemRefundDispatchConsumer>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => sut.Handle(Event(txId), CancellationToken.None));
    }

    // ---------- helpers ----------

    private static TradeOfferDispatchResult SentResult()
        => new(TradeOfferDispatchStatus.Sent, "r", false, 1, null);

    private ItemRefundToSellerRequestedEvent Event(Guid txId)
        => new(Guid.NewGuid(), txId, _seller.Id, ItemRefundTrigger.TimeoutPayment, DateTime.UtcNow);

    private async Task<Guid> SeedTransactionAsync(bool escrowed)
    {
        var id = Guid.NewGuid();
        await using var arrange = CreateContext();
        arrange.Set<Transaction>().Add(new Transaction
        {
            Id = id,
            Status = TransactionStatus.CANCELLED_TIMEOUT,
            CancelledBy = CancelledByType.TIMEOUT,
            CancelReason = "Payment timeout",
            CancelledAt = DateTime.UtcNow,
            SellerId = _seller.Id,
            EscrowBotId = escrowed ? _bot.Id : null,
            EscrowBotAssetId = escrowed ? "bot-asset-x" : null,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000402",
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
}
