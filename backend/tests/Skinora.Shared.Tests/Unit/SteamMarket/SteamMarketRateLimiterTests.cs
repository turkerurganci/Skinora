using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.SteamMarket;
using Xunit;

namespace Skinora.Shared.Tests.Unit.SteamMarket;

public class SteamMarketRateLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AcquireAsync_FirstCall_DoesNotWait()
    {
        var clock = new FakeTimeProvider(Start);
        var limiter = new SteamMarketRateLimiter(limit: 20, clock);

        await limiter.AcquireAsync(default);
    }

    [Fact]
    public async Task AcquireAsync_UnderLimit_AllAcquireImmediately()
    {
        var clock = new FakeTimeProvider(Start);
        var limiter = new SteamMarketRateLimiter(limit: 5, clock);

        for (var i = 0; i < 5; i++)
        {
            await limiter.AcquireAsync(default);
        }
    }

    [Fact]
    public void RegisterRetryAfter_ZeroOrNegative_NoOp()
    {
        var clock = new FakeTimeProvider(Start);
        var limiter = new SteamMarketRateLimiter(limit: 5, clock);

        limiter.RegisterRetryAfter(TimeSpan.Zero);
        limiter.RegisterRetryAfter(TimeSpan.FromSeconds(-10));
    }

    [Fact]
    public async Task RegisterRetryAfter_BlocksNextAcquireUntilDeadline()
    {
        var clock = new FakeTimeProvider(Start);
        var limiter = new SteamMarketRateLimiter(limit: 5, clock);

        limiter.RegisterRetryAfter(TimeSpan.FromMinutes(2));

        var acquireTask = limiter.AcquireAsync(default);
        Assert.False(acquireTask.IsCompleted);

        clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        await acquireTask;
    }

    [Fact]
    public async Task RegisterRetryAfter_LongerDeadline_TakesPrecedence()
    {
        var clock = new FakeTimeProvider(Start);
        var limiter = new SteamMarketRateLimiter(limit: 5, clock);

        limiter.RegisterRetryAfter(TimeSpan.FromSeconds(10));
        limiter.RegisterRetryAfter(TimeSpan.FromSeconds(60));
        limiter.RegisterRetryAfter(TimeSpan.FromSeconds(5));

        var acquireTask = limiter.AcquireAsync(default);

        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.False(acquireTask.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(50));
        await acquireTask;
    }

    [Fact]
    public async Task AcquireAsync_WindowFull_DropsExpiredEntries()
    {
        var clock = new FakeTimeProvider(Start);
        var limiter = new SteamMarketRateLimiter(limit: 3, clock);

        await limiter.AcquireAsync(default);
        await limiter.AcquireAsync(default);
        await limiter.AcquireAsync(default);

        clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));

        await limiter.AcquireAsync(default);
    }

    [Fact]
    public async Task AcquireAsync_WindowFull_WaitsUntilOldestExits()
    {
        var clock = new FakeTimeProvider(Start);
        var limiter = new SteamMarketRateLimiter(limit: 2, clock);

        // Two immediate acquires fill the window.
        await limiter.AcquireAsync(default);
        await limiter.AcquireAsync(default);

        // Third acquire must wait until the oldest entry (at Start)
        // ages out — that is, until Start + 60s + epsilon.
        var third = limiter.AcquireAsync(default);
        Assert.False(third.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(60) + TimeSpan.FromMilliseconds(1));
        await third;
    }

    [Fact]
    public void Constructor_NonPositiveLimit_Throws()
    {
        var clock = new FakeTimeProvider(Start);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SteamMarketRateLimiter(limit: 0, clock));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SteamMarketRateLimiter(limit: -1, clock));
    }

    [Fact]
    public void Constructor_NullClock_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SteamMarketRateLimiter(limit: 1, timeProvider: null!));
    }
}
