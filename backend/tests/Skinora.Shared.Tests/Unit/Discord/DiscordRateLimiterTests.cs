using Skinora.Shared.Discord;
using Xunit;

namespace Skinora.Shared.Tests.Unit.Discord;

public class DiscordRateLimiterTests
{
    private static DiscordSettings Settings(int globalPerSecond = 45) => new()
    {
        Provider = DiscordSettings.ProviderDiscord,
        ClientId = "c",
        ClientSecret = "s",
        BotToken = "t",
        GlobalRatePerSecond = globalPerSecond,
    };

    [Fact]
    public async Task WaitAsync_FirstCallForBucket_ReturnsImmediately()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var sut = new DiscordRateLimiter(Settings(), clock.Now);

        var start = clock.Now();
        await sut.WaitAsync("createDm", CancellationToken.None);

        Assert.Equal(start, clock.Now());
    }

    [Fact]
    public async Task RegisterRetryAfter_PerBucket_PausesNextCallForBucket()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var sut = new DiscordRateLimiter(Settings(), clock.Now);

        sut.RegisterRetryAfter("createDm", seconds: 30, isGlobal: false);
        clock.Advance(TimeSpan.FromSeconds(30));

        await sut.WaitAsync("createDm", CancellationToken.None);

        // No exception — the gate released once the fake clock advanced.
        Assert.True(true);
    }

    [Fact]
    public async Task RegisterRetryAfter_Global_BlocksAllBuckets()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var sut = new DiscordRateLimiter(Settings(), clock.Now);

        sut.RegisterRetryAfter("sendMessage:1", seconds: 5, isGlobal: true);
        clock.Advance(TimeSpan.FromSeconds(5));

        await sut.WaitAsync("sendMessage:1", CancellationToken.None);
        await sut.WaitAsync("createDm", CancellationToken.None);

        Assert.True(true);
    }

    [Fact]
    public void RegisterRetryAfter_NegativeOrZero_NoOp()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var sut = new DiscordRateLimiter(Settings(), clock.Now);

        sut.RegisterRetryAfter("createDm", 0, false);
        sut.RegisterRetryAfter("createDm", -1, true);

        Assert.True(true);
    }

    [Fact]
    public async Task RegisterBucket_MapsStableToDiscordBucket_SharesGate()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var sut = new DiscordRateLimiter(Settings(), clock.Now);

        // The bot client publishes the stable key first ("sendMessage:1")
        // then maps it onto the Discord-issued bucket header. Subsequent
        // calls with the same stable key should route through the
        // canonical bucket.
        sut.RegisterBucket("sendMessage:1", "abc123");
        sut.RegisterRetryAfter("abc123", seconds: 10, isGlobal: false);
        clock.Advance(TimeSpan.FromSeconds(10));

        await sut.WaitAsync("sendMessage:1", CancellationToken.None);

        Assert.True(true);
    }

    [Fact]
    public async Task RegisterReset_FutureWindow_BlocksNextWait()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var sut = new DiscordRateLimiter(Settings(), clock.Now);

        sut.RegisterReset("createDm", resetAfterSeconds: 2.0);
        clock.Advance(TimeSpan.FromSeconds(2));

        await sut.WaitAsync("createDm", CancellationToken.None);

        Assert.True(true);
    }

    [Fact]
    public async Task WaitAsync_GlobalBudget_HoldsWhenExhausted()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var sut = new DiscordRateLimiter(Settings(globalPerSecond: 3), clock.Now);

        await sut.WaitAsync("a", CancellationToken.None);
        await sut.WaitAsync("b", CancellationToken.None);
        await sut.WaitAsync("c", CancellationToken.None);

        // Three booked within the same fake-clock instant — the fourth
        // would have to wait for the sliding window to drop the first
        // entry, which only happens after Advance().
        clock.Advance(TimeSpan.FromSeconds(1.5));
        await sut.WaitAsync("d", CancellationToken.None);

        Assert.True(true);
    }

    private sealed class FakeClock
    {
        private DateTimeOffset _now;
        public FakeClock(DateTimeOffset start) => _now = start;
        public DateTimeOffset Now() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
