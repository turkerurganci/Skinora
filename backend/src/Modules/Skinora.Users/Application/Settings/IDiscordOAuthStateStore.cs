namespace Skinora.Users.Application.Settings;

/// <summary>
/// Short-lived state-correlation store for the Discord OAuth round-trip
/// (07 §5.12, §5.13). The connect endpoint generates a random state token,
/// persists <c>{state -&gt; userId}</c> with a TTL, and ships the state in
/// the outgoing Discord authorize URL. The callback verifies and atomically
/// consumes the state, which makes CSRF on the bind step impossible: an
/// attacker's state won't be in the store.
/// </summary>
public interface IDiscordOAuthStateStore
{
    Task IssueAsync(string state, Guid userId, TimeSpan ttl, CancellationToken cancellationToken);
    Task<Guid?> ConsumeAsync(string state, CancellationToken cancellationToken);
}
