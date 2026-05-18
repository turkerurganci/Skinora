using System.Collections.Concurrent;

namespace Skinora.Shared.Discord;

/// <summary>
/// In-process fallback for <see cref="IDiscordDmChannelCache"/> used by
/// integration tests (and by single-replica dev environments without
/// Redis). Expiry is enforced lazily on read; the cache is not designed
/// for hot-path production load.
/// </summary>
public sealed class InMemoryDiscordDmChannelCache : IDiscordDmChannelCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock;

    public InMemoryDiscordDmChannelCache()
        : this(() => DateTimeOffset.UtcNow)
    {
    }

    public InMemoryDiscordDmChannelCache(Func<DateTimeOffset> clock)
    {
        _clock = clock;
    }

    public Task<string?> GetAsync(string discordUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(discordUserId))
        {
            return Task.FromResult<string?>(null);
        }

        if (!_entries.TryGetValue(discordUserId, out var entry))
        {
            return Task.FromResult<string?>(null);
        }

        if (entry.ExpiresAtUtc <= _clock())
        {
            _entries.TryRemove(discordUserId, out _);
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(entry.ChannelId);
    }

    public Task SetAsync(
        string discordUserId, string channelId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(discordUserId)
            || string.IsNullOrWhiteSpace(channelId))
        {
            return Task.CompletedTask;
        }

        _entries[discordUserId] = new Entry(channelId, _clock().Add(ttl));
        return Task.CompletedTask;
    }

    public Task ForgetAsync(string discordUserId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(discordUserId))
        {
            _entries.TryRemove(discordUserId, out _);
        }

        return Task.CompletedTask;
    }

    private sealed record Entry(string ChannelId, DateTimeOffset ExpiresAtUtc);
}
