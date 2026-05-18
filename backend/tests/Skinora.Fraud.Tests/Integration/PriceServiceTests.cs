using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Skinora.Fraud.Application.Pricing;
using Skinora.Fraud.Domain.Entities;
using Skinora.Fraud.Infrastructure.Persistence;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Persistence;
using Skinora.Shared.SteamMarket;
using Skinora.Shared.Tests.Integration;

namespace Skinora.Fraud.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="PriceService"/> — T81. Exercises the
/// cache TTL state machine (08 §7.3) against a real SQL Server, plus the
/// 08 §7.4 fallback paths when the Steam Market transport fails.
/// </summary>
public class PriceServiceTests : IntegrationTestBase
{
    private const string ItemName = "AK-47 | Redline (Field-Tested)";

    static PriceServiceTests()
    {
        FraudModuleDbRegistration.RegisterFraudModule();
    }

    [Fact]
    public async Task GetMarketPriceAsync_CacheMiss_FetchesAndUpserts()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero));
        var client = new Mock<ISteamMarketPriceClient>(MockBehavior.Strict);
        client
            .Setup(c => c.GetPriceAsync(ItemName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SteamMarketPriceQuote.Median(13.10m, 12.50m));
        var scheduler = new SpyScheduler();

        var sut = BuildService(Context, client.Object, scheduler, clock);

        var result = await sut.GetMarketPriceAsync(ItemName, default);

        Assert.Equal(13.10m, result);
        client.VerifyAll();
        Assert.Empty(scheduler.Enqueued);

        await using var fresh = CreateContext();
        var cached = await fresh.Set<ItemPriceCache>().SingleAsync();
        Assert.Equal(ItemName, cached.MarketHashName);
        Assert.Equal(13.10m, cached.MedianPrice);
        Assert.Equal(12.50m, cached.LowestPrice);
        Assert.Equal(ItemPriceSources.SteamMarket, cached.Source);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, cached.FetchedAt);
    }

    [Fact]
    public async Task GetMarketPriceAsync_FreshCache_ReturnsCachedNoApiCall()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero));
        await SeedCacheAsync(median: 13.10m, lowest: 12.50m, fetchedHoursAgo: 6, clock);

        var client = new Mock<ISteamMarketPriceClient>(MockBehavior.Strict);
        var scheduler = new SpyScheduler();
        var sut = BuildService(CreateContext(), client.Object, scheduler, clock);

        var result = await sut.GetMarketPriceAsync(ItemName, default);

        Assert.Equal(13.10m, result);
        client.Verify(c => c.GetPriceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(scheduler.Enqueued);
    }

    [Fact]
    public async Task GetMarketPriceAsync_StaleCache_ReturnsCachedAndEnqueues()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero));
        await SeedCacheAsync(median: 13.10m, lowest: 12.50m, fetchedHoursAgo: 36, clock);

        var client = new Mock<ISteamMarketPriceClient>(MockBehavior.Strict);
        var scheduler = new SpyScheduler();
        var sut = BuildService(CreateContext(), client.Object, scheduler, clock);

        var result = await sut.GetMarketPriceAsync(ItemName, default);

        Assert.Equal(13.10m, result);
        // Synchronous path must not hit the API on a stale read.
        client.Verify(c => c.GetPriceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(scheduler.Enqueued);
    }

    [Fact]
    public async Task GetMarketPriceAsync_ExpiredCache_FetchesAndUpdatesRow()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero));
        await SeedCacheAsync(median: 13.10m, lowest: null, fetchedHoursAgo: 72, clock);

        var client = new Mock<ISteamMarketPriceClient>();
        client
            .Setup(c => c.GetPriceAsync(ItemName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SteamMarketPriceQuote.Median(15.00m));
        var scheduler = new SpyScheduler();
        var sut = BuildService(CreateContext(), client.Object, scheduler, clock);

        var result = await sut.GetMarketPriceAsync(ItemName, default);

        Assert.Equal(15.00m, result);

        await using var fresh = CreateContext();
        var cached = await fresh.Set<ItemPriceCache>().SingleAsync();
        Assert.Equal(15.00m, cached.MedianPrice);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, cached.FetchedAt);
    }

    [Fact]
    public async Task GetMarketPriceAsync_ApiTransientAndCacheTooOld_ReturnsNull()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero));
        await SeedCacheAsync(median: 5m, lowest: null, fetchedHoursAgo: 100, clock);

        var client = new Mock<ISteamMarketPriceClient>();
        client
            .Setup(c => c.GetPriceAsync(ItemName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SteamMarketTransientException("steam down"));
        var scheduler = new SpyScheduler();
        var sut = BuildService(CreateContext(), client.Object, scheduler, clock);

        var result = await sut.GetMarketPriceAsync(ItemName, default);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetMarketPriceAsync_ApiTransientAndNoCache_ReturnsNull()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero));
        var client = new Mock<ISteamMarketPriceClient>();
        client
            .Setup(c => c.GetPriceAsync(ItemName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SteamMarketTransientException("steam down"));
        var scheduler = new SpyScheduler();
        var sut = BuildService(Context, client.Object, scheduler, clock);

        var result = await sut.GetMarketPriceAsync(ItemName, default);

        Assert.Null(result);
        Assert.Empty(await Context.Set<ItemPriceCache>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task GetMarketPriceAsync_NoPriceQuote_CachesNullsAndReturnsNull()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero));
        var client = new Mock<ISteamMarketPriceClient>();
        client
            .Setup(c => c.GetPriceAsync(ItemName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SteamMarketPriceQuote.NoPrice());
        var scheduler = new SpyScheduler();
        var sut = BuildService(Context, client.Object, scheduler, clock);

        var result = await sut.GetMarketPriceAsync(ItemName, default);

        Assert.Null(result);

        await using var fresh = CreateContext();
        var cached = await fresh.Set<ItemPriceCache>().SingleAsync();
        Assert.Null(cached.MedianPrice);
        Assert.Null(cached.LowestPrice);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, cached.FetchedAt);
    }

    [Fact]
    public async Task GetMarketPriceAsync_LowestOnly_ReturnsLowestAndCaches()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero));
        var client = new Mock<ISteamMarketPriceClient>();
        client
            .Setup(c => c.GetPriceAsync(ItemName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SteamMarketPriceQuote.Lowest(7.25m));
        var scheduler = new SpyScheduler();
        var sut = BuildService(Context, client.Object, scheduler, clock);

        var result = await sut.GetMarketPriceAsync(ItemName, default);

        Assert.Equal(7.25m, result);

        await using var fresh = CreateContext();
        var cached = await fresh.Set<ItemPriceCache>().SingleAsync();
        Assert.Null(cached.MedianPrice);
        Assert.Equal(7.25m, cached.LowestPrice);
    }

    [Fact]
    public async Task RefreshAsync_SwallowsTransportException()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero));
        var client = new Mock<ISteamMarketPriceClient>();
        client
            .Setup(c => c.GetPriceAsync(ItemName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SteamMarketTransientException("background fail"));
        var sut = BuildService(Context, client.Object, new SpyScheduler(), clock);

        // Must not throw — refresh background entry point swallows.
        await sut.RefreshAsync(ItemName);
    }

    [Fact]
    public async Task RefreshAsync_UpsertsFreshQuote()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero));
        await SeedCacheAsync(median: 5m, lowest: null, fetchedHoursAgo: 30, clock);

        var client = new Mock<ISteamMarketPriceClient>();
        client
            .Setup(c => c.GetPriceAsync(ItemName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SteamMarketPriceQuote.Median(20m));
        var sut = BuildService(CreateContext(), client.Object, new SpyScheduler(), clock);

        await sut.RefreshAsync(ItemName);

        await using var fresh = CreateContext();
        var cached = await fresh.Set<ItemPriceCache>().SingleAsync();
        Assert.Equal(20m, cached.MedianPrice);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, cached.FetchedAt);
    }

    // --- helpers ---

    private static PriceService BuildService(
        AppDbContext db,
        ISteamMarketPriceClient client,
        IBackgroundJobScheduler scheduler,
        TimeProvider clock)
    {
        var settings = Options.Create(new SteamMarketSettings
        {
            FreshTtlHours = 24,
            StaleTtlHours = 48,
        });

        return new PriceService(
            db,
            client,
            scheduler,
            settings,
            clock,
            NullLogger<PriceService>.Instance);
    }

    private async Task SeedCacheAsync(
        decimal? median,
        decimal? lowest,
        int fetchedHoursAgo,
        TimeProvider clock)
    {
        await using var ctx = CreateContext();
        ctx.Set<ItemPriceCache>().Add(new ItemPriceCache
        {
            Id = Guid.NewGuid(),
            MarketHashName = ItemName,
            MedianPrice = median,
            LowestPrice = lowest,
            FetchedAt = clock.GetUtcNow().UtcDateTime - TimeSpan.FromHours(fetchedHoursAgo),
            Source = ItemPriceSources.SteamMarket,
        });
        await ctx.SaveChangesAsync();
    }

    private sealed class SpyScheduler : IBackgroundJobScheduler
    {
        public List<string> Enqueued { get; } = new();

        public string Enqueue<T>(Expression<Action<T>> methodCall)
        {
            Enqueued.Add(methodCall.ToString());
            return Guid.NewGuid().ToString("N");
        }

        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay) =>
            throw new NotImplementedException();

        public bool Delete(string jobId) => throw new NotImplementedException();

        public void AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>> methodCall, string cronExpression) =>
            throw new NotImplementedException();
    }
}
