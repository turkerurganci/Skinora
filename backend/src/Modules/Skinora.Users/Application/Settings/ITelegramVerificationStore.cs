namespace Skinora.Users.Application.Settings;

/// <summary>
/// Short-lived store for Telegram <c>SKN-XXXXXX</c> connect codes (07 §5.11).
/// The user issues a code from the UI, pastes <c>/start SKN-XXXXXX</c> into
/// the Telegram bot, and the webhook handler (07 §5.11b) redeems the code
/// atomically to bind the Telegram account to the user.
/// </summary>
public interface ITelegramVerificationStore
{
    /// <summary>Issues a code and maps it to the user with a TTL.</summary>
    Task IssueAsync(
        string code, Guid userId, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically reads and removes the user id bound to the code, or <c>null</c>
    /// if the code is unknown, expired, or already consumed. Mirrors the
    /// <c>GETDEL</c> contract in <c>RedisReAuthTokenStore</c>.
    /// </summary>
    Task<Guid?> ConsumeAsync(string code, CancellationToken cancellationToken);
}
