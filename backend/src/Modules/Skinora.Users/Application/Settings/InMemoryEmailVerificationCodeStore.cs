using System.Collections.Concurrent;

namespace Skinora.Users.Application.Settings;

/// <summary>
/// In-process store for tests. Expiry is checked at consume time rather than
/// via a background sweeper — sufficient for test lifetimes.
/// </summary>
public sealed class InMemoryEmailVerificationCodeStore : IEmailVerificationCodeStore
{
    private readonly ConcurrentDictionary<Guid, CodeEntry> _codes = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _cooldowns = new();
    private readonly TimeProvider _clock;

    public InMemoryEmailVerificationCodeStore(TimeProvider clock)
    {
        _clock = clock;
    }

    public Task IssueAsync(
        Guid userId, string code, string emailAddress, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _codes[userId] = new CodeEntry(code, emailAddress, _clock.GetUtcNow().Add(ttl));
        return Task.CompletedTask;
    }

    public Task<EmailVerificationCodeEntry?> PeekAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!_codes.TryGetValue(userId, out var entry))
            return Task.FromResult<EmailVerificationCodeEntry?>(null);

        if (_clock.GetUtcNow() >= entry.ExpiresAt)
        {
            _codes.TryRemove(userId, out _);
            return Task.FromResult<EmailVerificationCodeEntry?>(null);
        }

        return Task.FromResult<EmailVerificationCodeEntry?>(
            new EmailVerificationCodeEntry(entry.Code, entry.Email));
    }

    public Task<EmailVerificationCodeEntry?> ConsumeAsync(
        Guid userId, string candidate, CancellationToken cancellationToken)
    {
        if (!_codes.TryRemove(userId, out var entry))
            return Task.FromResult<EmailVerificationCodeEntry?>(null);

        if (_clock.GetUtcNow() >= entry.ExpiresAt)
            return Task.FromResult<EmailVerificationCodeEntry?>(null);

        if (!string.Equals(entry.Code, candidate, StringComparison.Ordinal))
            return Task.FromResult<EmailVerificationCodeEntry?>(null);

        return Task.FromResult<EmailVerificationCodeEntry?>(
            new EmailVerificationCodeEntry(entry.Code, entry.Email));
    }

    public Task<TimeSpan?> GetCooldownAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!_cooldowns.TryGetValue(userId, out var expiresAt))
            return Task.FromResult<TimeSpan?>(null);

        var remaining = expiresAt - _clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            _cooldowns.TryRemove(userId, out _);
            return Task.FromResult<TimeSpan?>(null);
        }

        return Task.FromResult<TimeSpan?>(remaining);
    }

    public Task RecordSendAsync(Guid userId, TimeSpan cooldown, CancellationToken cancellationToken)
    {
        _cooldowns[userId] = _clock.GetUtcNow().Add(cooldown);
        return Task.CompletedTask;
    }

    private sealed record CodeEntry(string Code, string Email, DateTimeOffset ExpiresAt);
}
