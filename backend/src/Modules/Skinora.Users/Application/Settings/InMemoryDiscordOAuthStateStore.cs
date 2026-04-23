using System.Collections.Concurrent;

namespace Skinora.Users.Application.Settings;

public sealed class InMemoryDiscordOAuthStateStore : IDiscordOAuthStateStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public InMemoryDiscordOAuthStateStore(TimeProvider clock)
    {
        _clock = clock;
    }

    public Task IssueAsync(string state, Guid userId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _entries[state] = new Entry(userId, _clock.GetUtcNow().Add(ttl));
        return Task.CompletedTask;
    }

    public Task<Guid?> ConsumeAsync(string state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state) || !_entries.TryRemove(state, out var entry))
            return Task.FromResult<Guid?>(null);

        if (_clock.GetUtcNow() >= entry.ExpiresAt)
            return Task.FromResult<Guid?>(null);

        return Task.FromResult<Guid?>(entry.UserId);
    }

    private sealed record Entry(Guid UserId, DateTimeOffset ExpiresAt);
}
