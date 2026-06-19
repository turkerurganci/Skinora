using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Application.Trade;
using Skinora.Steam.Domain.Entities;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Steam.Tests.Integration;

/// <summary>
/// Integration coverage for <see cref="SteamTradeOfferUrlResolver"/>
/// (WP12, T90 K3 — 04 §7.3 "Steam'e git linki"). Verifies the resolver picks
/// the latest sent offer of the requested direction that carries a
/// SteamTradeOfferId, filters by direction, and returns null when no such
/// offer exists.
/// </summary>
public class SteamTradeOfferUrlResolverTests : IntegrationTestBase
{
    static SteamTradeOfferUrlResolverTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
    }

    private User _seller = null!;
    private Transaction _transaction = null!;
    private PlatformSteamBot _bot = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000301",
            SteamDisplayName = "Seller",
        };
        context.Set<User>().Add(_seller);

        _bot = new PlatformSteamBot
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198099999301",
            DisplayName = "EscrowBot-Url",
            Status = PlatformSteamBotStatus.ACTIVE,
        };
        context.Set<PlatformSteamBot>().Add(_bot);
        await context.SaveChangesAsync();

        _transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.TRADE_OFFER_SENT_TO_SELLER,
            SellerId = _seller.Id,
            EscrowBotId = _bot.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000302",
            ItemAssetId = "12345678901",
            ItemClassId = "98765432101",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 50.000000m,
            CommissionRate = 0.0300m,
            CommissionAmount = 1.500000m,
            TotalAmount = 51.500000m,
            SellerPayoutAddress = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL",
            PaymentTimeoutMinutes = 60,
        };
        context.Set<Transaction>().Add(_transaction);
        await context.SaveChangesAsync();
    }

    private TradeOffer NewSentOffer(
        TradeOfferDirection direction, string? steamTradeOfferId, DateTime sentAt) => new()
        {
            Id = Guid.NewGuid(),
            TransactionId = _transaction.Id,
            PlatformSteamBotId = _bot.Id,
            Direction = direction,
            Status = TradeOfferStatus.SENT,
            SteamTradeOfferId = steamTradeOfferId,
            SentAt = sentAt,
            RetryCount = 0,
        };

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Resolves_Url_For_Sent_Offer_Of_Requested_Direction()
    {
        Context.Set<TradeOffer>().Add(
            NewSentOffer(TradeOfferDirection.TO_SELLER, "9988", DateTime.UtcNow));
        await Context.SaveChangesAsync();

        var sut = new SteamTradeOfferUrlResolver(Context);
        var url = await sut.ResolveUrlAsync(
            _transaction.Id, TradeOfferDirection.TO_SELLER, CancellationToken.None);

        Assert.Equal("https://steamcommunity.com/tradeoffer/9988/", url);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Returns_Null_When_No_Offer_Of_Direction_Has_SteamTradeOfferId()
    {
        // Offer exists but in the wrong direction; the requested direction's
        // only row carries no Steam id yet (dispatch still PENDING-equivalent).
        Context.Set<TradeOffer>().Add(
            NewSentOffer(TradeOfferDirection.TO_BUYER, "5555", DateTime.UtcNow));
        Context.Set<TradeOffer>().Add(new TradeOffer
        {
            Id = Guid.NewGuid(),
            TransactionId = _transaction.Id,
            PlatformSteamBotId = _bot.Id,
            Direction = TradeOfferDirection.TO_SELLER,
            Status = TradeOfferStatus.PENDING,
            SteamTradeOfferId = null,
            RetryCount = 0,
        });
        await Context.SaveChangesAsync();

        var sut = new SteamTradeOfferUrlResolver(Context);
        var url = await sut.ResolveUrlAsync(
            _transaction.Id, TradeOfferDirection.TO_SELLER, CancellationToken.None);

        Assert.Null(url);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Picks_Latest_Sent_Offer_When_Multiple_Of_Same_Direction()
    {
        var older = DateTime.UtcNow.AddMinutes(-30);
        var newer = DateTime.UtcNow;
        Context.Set<TradeOffer>().Add(NewSentOffer(TradeOfferDirection.TO_BUYER, "1111", older));
        Context.Set<TradeOffer>().Add(NewSentOffer(TradeOfferDirection.TO_BUYER, "2222", newer));
        await Context.SaveChangesAsync();

        var sut = new SteamTradeOfferUrlResolver(Context);
        var url = await sut.ResolveUrlAsync(
            _transaction.Id, TradeOfferDirection.TO_BUYER, CancellationToken.None);

        Assert.Equal("https://steamcommunity.com/tradeoffer/2222/", url);
    }
}
