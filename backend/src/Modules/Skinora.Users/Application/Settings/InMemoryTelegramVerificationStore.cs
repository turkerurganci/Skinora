using System.Collections.Concurrent;

namespace Skinora.Users.Application.Settings;

public sealed class InMemoryTelegramVerificationStore : ITelegramVerificationStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, FailEntry> _failures = new();
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

    public Task<int> RegisterFailedAttemptAsync(
        long telegramUserId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var expiresAt = _clock.GetUtcNow().Add(ttl);
        var updated = _failures.AddOrUpdate(
            telegramUserId,
            _ => new FailEntry(1, expiresAt),
            (_, existing) =>
            {
                if (_clock.GetUtcNow() >= existing.ExpiresAt)
                {
                    return new FailEntry(1, expiresAt);
                }

                return new FailEntry(existing.Count + 1, existing.ExpiresAt);
            });
        return Task.FromResult(updated.Count);
    }

    public Task<int> GetFailedAttemptsAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        if (!_failures.TryGetValue(telegramUserId, out var entry))
            return Task.FromResult(0);

        if (_clock.GetUtcNow() >= entry.ExpiresAt)
        {
            _failures.TryRemove(telegramUserId, out _);
            return Task.FromResult(0);
        }

        return Task.FromResult(entry.Count);
    }

    private sealed record Entry(Guid UserId, DateTimeOffset ExpiresAt);
    private sealed record FailEntry(int Count, DateTimeOffset ExpiresAt);
}
