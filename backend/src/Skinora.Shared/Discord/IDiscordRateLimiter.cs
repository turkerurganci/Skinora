namespace Skinora.Shared.Discord;

/// <summary>
/// Per-bucket + global rate gate for Discord bot API calls (08 §6.3).
/// Discord publishes the rate-limit metadata in response headers
/// (<c>X-RateLimit-Bucket</c>, <c>X-RateLimit-Reset-After</c>,
/// <c>Retry-After</c>); the gate is header-driven rather than driven
/// by hard-coded constants so the platform tracks Discord's
/// runtime-adjusted limits without code changes.
/// </summary>
/// <remarks>
/// <para>
/// The bot client calls <see cref="WaitAsync"/> before every HTTP POST,
/// then feeds the response back through <see cref="RegisterBucket"/>,
/// <see cref="RegisterReset"/> and (on 429)
/// <see cref="RegisterRetryAfter"/>. The bucket id flowing through
/// <see cref="WaitAsync"/> is the call-site stable key
/// (<c>createDm</c>, <c>sendMessage:{channelId}</c>); the Discord-issued
/// bucket header is mapped onto the same stable key via
/// <see cref="RegisterBucket"/> so the next call routes through the
/// canonical Discord bucket.
/// </para>
/// </remarks>
public interface IDiscordRateLimiter
{
    /// <summary>
    /// Awaits the per-bucket + global gates. Returns when the caller
    /// is free to issue the Discord request.
    /// </summary>
    Task WaitAsync(string bucket, CancellationToken cancellationToken);

    /// <summary>
    /// Honours a Discord <c>retry_after</c> by holding the bucket gate
    /// (or the global gate when <paramref name="isGlobal"/> is set) for
    /// the specified seconds.
    /// </summary>
    void RegisterRetryAfter(string bucket, double seconds, bool isGlobal);

    /// <summary>
    /// Maps a route-level stable key onto the Discord-issued bucket id
    /// (<c>X-RateLimit-Bucket</c>). Subsequent <see cref="WaitAsync"/>
    /// calls with the same stable key honour any reset/retry-after
    /// recorded against the bucket header value as well.
    /// </summary>
    void RegisterBucket(string bucket, string discordBucket);

    /// <summary>
    /// Records the <c>X-RateLimit-Reset-After</c> hint so the next
    /// call against the same bucket waits the remaining window when
    /// the remaining count was already exhausted by the current
    /// request.
    /// </summary>
    void RegisterReset(string bucket, double resetAfterSeconds);
}
