using System.Collections.Concurrent;

namespace Skinora.Users.Application.Settings;

public sealed class InMemoryTelegramVerificationStore : ITelegramVerificationStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public InMemoryTelegramVerificationStore(TimeProvider clock)
    {
        _clock = clock;
    }

    public Task IssueAsync(string code, Guid userId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _entries[code] = new Entry(userId, _clock.GetUtcNow().Add(ttl));
        return Task.CompletedTask;
    }

    public Task<Guid?> ConsumeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || !_entries.TryRemove(code, out var entry))
            return Task.FromResult<Guid?>(null);

        if (_clock.GetUtcNow() >= entry.ExpiresAt)
            return Task.FromResult<Guid?>(null);

        return Task.FromResult<Guid?>(entry.UserId);
    }

    private sealed record Entry(Guid UserId, DateTimeOffset ExpiresAt);
}
