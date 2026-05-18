namespace Skinora.Users.Application.Settings;

/// <summary>
/// Short-lived store for Telegram connect codes (07 §5.11, 08 §5.1).
/// The user issues an opaque code from the UI, pastes <c>/start {code}</c>
/// into the Telegram bot, and the webhook handler (07 §5.11b) redeems
/// the code atomically to bind the Telegram account to the user.
/// </summary>
/// <remarks>
/// <para>
/// T79 added a per-Telegram-user attempt counter to satisfy the
/// 08 §5.1 brute-force protection requirement (5 failed redemptions →
/// the next request from the same Telegram user is silently ignored
/// until the counter TTL expires).
/// </para>
/// </remarks>
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

    /// <summary>
    /// Increments the failed-attempt counter for a Telegram user and
    /// returns the new value. The counter is bound to the same TTL as
    /// the connect code so a slow brute-force resets after the user
    /// re-issues a code.
    /// </summary>
    Task<int> RegisterFailedAttemptAsync(
        long telegramUserId, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the current failed-attempt counter for a Telegram user
    /// without incrementing it. Used by the brute-force gate before
    /// each <see cref="ConsumeAsync"/>.
    /// </summary>
    Task<int> GetFailedAttemptsAsync(
        long telegramUserId, CancellationToken cancellationToken);
}
