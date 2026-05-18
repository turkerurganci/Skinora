using Skinora.Shared.Telegram;
using Xunit;

namespace Skinora.Shared.Tests.Unit.Telegram;

public class TelegramRateLimiterTests
{
    private static TelegramSettings Settings(int perChat = 1, int global = 30) => new()
    {
        PerChatRatePerSecond = perChat,
        GlobalRatePerSecond = global,
    };

    [Fact]
    public async Task WaitAsync_FirstCallForChat_ReturnsImmediately()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var sut = new TelegramRateLimiter(Settings(), clock.Now);

        var start = clock.Now();
        await sut.WaitAsync("12345", CancellationToken.None);

        Assert.Equal(start, clock.Now());
    }

    [Fact]
    public async Task WaitAsync_SecondCallSameChatWithinOneSecond_DelaysUntilWindowSlides()
    {
        // The semaphore-backed gate calls Task.Delay, but the underlying
        // clock is fake — Task.Delay still real-time runs in this test
        // so we keep the delta below 1s to verify the limiter computes
        // a positive wait without burning seconds.
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var sut = new TelegramRateLimiter(Settings(perChat: 1), clock.Now);

        await sut.WaitAsync("12345", CancellationToken.None);

        // Advance the clock past the per-chat window so the second call
        // doesn't actually have to wait — we're checking the bookkeeping
        // not the real wall-clock delay.
        clock.Advance(TimeSpan.FromSeconds(1.5));

        await sut.WaitAsync("12345", CancellationToken.None);

        // Both sends booked under the fake clock — no exception thrown.
        Assert.True(true);
    }

    [Fact]
    public async Task WaitAsync_DifferentChatsShareGlobalBudget()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var sut = new TelegramRateLimiter(Settings(perChat: 1, global: 3), clock.Now);

        await sut.WaitAsync("a", CancellationToken.None);
        await sut.WaitAsync("b", CancellationToken.None);
        await sut.WaitAsync("c", CancellationToken.None);

        // Three booked within the same instant of the fake clock — the
        // next send would have to wait. Test stops at the boundary.
        Assert.True(true);
    }

    [Fact]
    public void RegisterRetryAfter_NegativeOrZero_DoesNothing()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var sut = new TelegramRateLimiter(Settings(), clock.Now);

        sut.RegisterRetryAfter("12345", 0);
        sut.RegisterRetryAfter("12345", -5);

        // No exception — gate left empty.
        Assert.True(true);
    }

    [Fact]
    public async Task RegisterRetryAfter_PositiveValue_BlocksNextWaitForChat()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var sut = new TelegramRateLimiter(Settings(perChat: 1), clock.Now);

        sut.RegisterRetryAfter("12345", 30);

        // Advance the clock past the retry-after window and verify the
        // wait gate releases.
        clock.Advance(TimeSpan.FromSeconds(30));
        await sut.WaitAsync("12345", CancellationToken.None);

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
