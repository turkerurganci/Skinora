using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Skinora.Shared.Discord;

/// <summary>
/// Header-driven rate limiter for Discord bot API calls (08 §6.3).
/// </summary>
/// <remarks>
/// <para>
/// Two gates run before every request:
/// </para>
/// <list type="number">
///   <item>
///     <b>Per-bucket gate</b> — a <see cref="SemaphoreSlim"/>(1,1) per
///     bucket key, plus a <c>NextSendAtUtc</c> timestamp that captures
///     the most recent <c>X-RateLimit-Reset-After</c> /
///     <c>Retry-After</c> hint. The semaphore preserves request
///     ordering within the bucket; the timestamp encodes the actual
///     wait-until time.
///   </item>
///   <item>
///     <b>Global gate</b> — a sliding window of recent send timestamps
///     enforces <see cref="DiscordSettings.GlobalRatePerSecond"/>
///     (defaults to 45/s under the documented ~50/s cap). When a 429
///     marked <c>global: true</c> arrives, the global gate is held
///     for the supplied <c>retry_after</c>.
///   </item>
/// </list>
/// <para>
/// The clock is injected as a <see cref="Func{DateTimeOffset}"/> so
/// unit tests can advance time deterministically without
/// <see cref="Task.Delay(TimeSpan)"/>.
/// </para>
/// </remarks>
public sealed class DiscordRateLimiter : IDiscordRateLimiter, IDisposable
{
    private readonly DiscordSettings _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, BucketGate> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _stableToDiscord = new(StringComparer.Ordinal);
    private readonly object _globalLock = new();
    private readonly LinkedList<DateTimeOffset> _globalSends = new();
    private DateTimeOffset? _globalRetryUntilUtc;

    public DiscordRateLimiter(IOptions<DiscordSettings> settings)
        : this(settings.Value, () => DateTimeOffset.UtcNow)
    {
    }

    public DiscordRateLimiter(DiscordSettings settings, Func<DateTimeOffset> clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public async Task WaitAsync(string bucket, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(bucket);

        var gate = ResolveGate(bucket);
        await gate.Semaphore.WaitAsync(cancellationToken);

        var released = false;
        try
        {
            await WaitForBucketAsync(gate, cancellationToken);
            await WaitForGlobalAsync(cancellationToken);

            var now = _clock();
            RecordGlobalSend(now);
        }
        finally
        {
            if (!released)
            {
                gate.Semaphore.Release();
            }
        }
    }

    public void RegisterRetryAfter(string bucket, double seconds, bool isGlobal)
    {
        if (seconds <= 0)
        {
            return;
        }

        if (isGlobal)
        {
            lock (_globalLock)
            {
                _globalRetryUntilUtc = _clock().AddSeconds(seconds);
            }

            return;
        }

        var gate = ResolveGate(bucket);
        var until = _clock().AddSeconds(seconds);
        if (gate.NextSendAtUtc is null || until > gate.NextSendAtUtc)
        {
            gate.NextSendAtUtc = until;
        }
    }

    public void RegisterBucket(string bucket, string discordBucket)
    {
        if (string.IsNullOrWhiteSpace(discordBucket))
        {
            return;
        }

        _stableToDiscord[bucket] = discordBucket;
    }

    public void RegisterReset(string bucket, double resetAfterSeconds)
    {
        if (resetAfterSeconds <= 0)
        {
            return;
        }

        var gate = ResolveGate(bucket);
        var until = _clock().AddSeconds(resetAfterSeconds);

        // The reset hint only matters when it's further in the future
        // than what we've already booked — otherwise a stale write
        // could re-open a bucket that a 429 just closed.
        if (gate.NextSendAtUtc is null || until > gate.NextSendAtUtc)
        {
            gate.NextSendAtUtc = until;
        }
    }

    private BucketGate ResolveGate(string bucket)
    {
        var canonical = _stableToDiscord.TryGetValue(bucket, out var discordBucket)
            ? discordBucket
            : bucket;

        return _gates.GetOrAdd(canonical, _ => new BucketGate());
    }

    private async Task WaitForBucketAsync(BucketGate gate, CancellationToken cancellationToken)
    {
        while (true)
        {
            var now = _clock();
            if (gate.NextSendAtUtc is { } next && next > now)
            {
                await Task.Delay(next - now, cancellationToken);
                continue;
            }

            return;
        }
    }

    private async Task WaitForGlobalAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_globalLock)
            {
                var now = _clock();
                if (_globalRetryUntilUtc is { } retry && retry > now)
                {
                    wait = retry - now;
                }
                else
                {
                    TrimGlobalSends(now);
                    if (_settings.GlobalRatePerSecond <= 0
                        || _globalSends.Count < _settings.GlobalRatePerSecond)
                    {
                        return;
                    }

                    wait = _globalSends.First!.Value.AddSeconds(1) - now;
                }
            }

            if (wait <= TimeSpan.Zero)
            {
                continue;
            }

            await Task.Delay(wait, cancellationToken);
        }
    }

    private void RecordGlobalSend(DateTimeOffset now)
    {
        lock (_globalLock)
        {
            _globalSends.AddLast(now);
            TrimGlobalSends(now);
        }
    }

    private void TrimGlobalSends(DateTimeOffset now)
    {
        var cutoff = now.AddSeconds(-1);
        while (_globalSends.First is { } first && first.Value <= cutoff)
        {
            _globalSends.RemoveFirst();
        }
    }

    public void Dispose()
    {
        foreach (var gate in _gates.Values)
        {
            gate.Semaphore.Dispose();
        }
    }

    private sealed class BucketGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public DateTimeOffset? NextSendAtUtc { get; set; }
    }
}
