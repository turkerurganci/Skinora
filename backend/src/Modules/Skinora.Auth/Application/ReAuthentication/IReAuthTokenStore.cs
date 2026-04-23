namespace Skinora.Auth.Application.ReAuthentication;

/// <summary>
/// Short-lived, single-use token store for the re-verify flow (07 §4.7).
/// The store hashes tokens internally (SHA-256) so plaintext is never at rest.
/// Typical TTL is 5 minutes; <see cref="ConsumeAsync"/> is atomic and
/// idempotent — the second consume of the same token returns <c>null</c>.
/// </summary>
public interface IReAuthTokenStore
{
    /// <summary>Persists the token payload under a SHA-256 hash with the given TTL.</summary>
    Task IssueAsync(
        string plainTextToken,
        ReAuthTokenPayload payload,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically reads and deletes the payload keyed by the SHA-256 hash of the
    /// supplied token. Returns <c>null</c> when the token is unknown, expired,
    /// or already consumed.
    /// </summary>
    Task<ReAuthTokenPayload?> ConsumeAsync(
        string plainTextToken,
        CancellationToken cancellationToken);
}

public sealed record ReAuthTokenPayload(Guid UserId, string SteamId);
