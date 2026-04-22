using System.Collections.Concurrent;

namespace Skinora.Auth.Application.ReAuthentication;

/// <summary>
/// In-process <see cref="IReAuthTokenStore"/> for tests. Indexes by SHA-256
/// hash (same as Redis impl) and enforces single-use via
/// <c>TryRemove</c>. TTL is checked at consume time, not with a background
/// sweeper, which is sufficient for test lifetimes.
/// </summary>
public sealed class InMemoryReAuthTokenStore : IReAuthTokenStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryReAuthTokenStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public Task IssueAsync(
        string plainTextToken,
        ReAuthTokenPayload payload,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        var key = ReAuthTokenHasher.Hash(plainTextToken);
        var expiresAt = _timeProvider.GetUtcNow().Add(ttl);
        _entries[key] = new Entry(payload, expiresAt);
        return Task.CompletedTask;
    }

    public Task<ReAuthTokenPayload?> ConsumeAsync(
        string plainTextToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(plainTextToken))
            return Task.FromResult<ReAuthTokenPayload?>(null);

        var key = ReAuthTokenHasher.Hash(plainTextToken);
        if (!_entries.TryRemove(key, out var entry))
            return Task.FromResult<ReAuthTokenPayload?>(null);

        if (_timeProvider.GetUtcNow() >= entry.ExpiresAt)
            return Task.FromResult<ReAuthTokenPayload?>(null);

        return Task.FromResult<ReAuthTokenPayload?>(entry.Payload);
    }

    private sealed record Entry(ReAuthTokenPayload Payload, DateTimeOffset ExpiresAt);
}
