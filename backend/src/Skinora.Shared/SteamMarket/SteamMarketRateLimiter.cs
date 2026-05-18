using Microsoft.Extensions.Options;

namespace Skinora.Shared.SteamMarket;

/// <summary>
/// Sliding-window throttle for the Steam Market <c>priceoverview</c>
/// endpoint (08 §7.1 — "~20 istek/dakika rate limit"). Tracks the
/// outbound timestamps in a queue and yields when the window is full;
/// honours a server-side <c>Retry-After</c> by pushing the next-allowed
/// instant forward.
/// </summary>
/// <remarks>
/// Registered as a singleton so all <see cref="SteamMarketPriceClient"/>
/// instances share the same window. The class is thread-safe via an
/// internal lock — the limiter is the bottleneck for the cache-miss
/// path, not the hot path, so contention here is bounded by
/// <see cref="SteamMarketSettings.RateLimitPerMinute"/>.
///
/// A <see cref="TimeProvider"/> is injected so unit tests can drive
/// deterministic sliding-window behaviour via <c>FakeTimeProvider</c>
/// — both the timestamp reads and the
/// <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>
/// wait flow through it.
/// </remarks>
public sealed class SteamMarketRateLimiter : ISteamMarketRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly int _limit;
    private readonly TimeProvider _timeProvider;
    private readonly Queue<DateTimeOffset> _recentCalls = new();
    private readonly object _lock = new();

    private DateTimeOffset _nextAllowedUtc = DateTimeOffset.MinValue;

    public SteamMarketRateLimiter(IOptions<SteamMarketSettings> options, TimeProvider timeProvider)
        : this(options.Value.RateLimitPerMinute, timeProvider)
    {
    }

    public SteamMarketRateLimiter(int limit, TimeProvider timeProvider)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "RateLimitPerMinute must be positive.");
        }

        _limit = limit;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task AcquireAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TimeSpan wait;
            lock (_lock)
            {
                var now = _timeProvider.GetUtcNow();
                PruneLocked(now);

                if (now < _nextAllowedUtc)
                {
                    wait = _nextAllowedUtc - now;
                }
                else if (_recentCalls.Count >= _limit)
                {
                    var oldest = _recentCalls.Peek();
                    wait = (oldest + Window) - now;
                    if (wait < TimeSpan.Zero)
                    {
                        wait = TimeSpan.Zero;
                    }
                }
                else
                {
                    _recentCalls.Enqueue(now);
                    return;
                }
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void RegisterRetryAfter(TimeSpan retryAfter)
    {
        if (retryAfter <= TimeSpan.Zero)
        {
            return;
        }

        lock (_lock)
        {
            var candidate = _timeProvider.GetUtcNow() + retryAfter;
            if (candidate > _nextAllowedUtc)
            {
                _nextAllowedUtc = candidate;
            }
        }
    }

    private void PruneLocked(DateTimeOffset now)
    {
        var cutoff = now - Window;
        while (_recentCalls.TryPeek(out var oldest) && oldest <= cutoff)
        {
            _recentCalls.Dequeue();
        }
    }
}
