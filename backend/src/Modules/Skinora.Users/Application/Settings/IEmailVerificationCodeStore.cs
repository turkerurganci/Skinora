namespace Skinora.Users.Application.Settings;

/// <summary>
/// Short-lived store for 6-digit email verification codes (07 §5.7, §5.8).
/// Codes hash to a fixed prefix keyed by <c>userId</c>; a single active code
/// exists per user at a time so re-sending replaces the prior code and resets
/// the TTL. A cooldown key is kept separately to rate-limit
/// <c>POST /email/send-verification</c>.
/// </summary>
public interface IEmailVerificationCodeStore
{
    /// <summary>
    /// Persists a plaintext verification code for the given user with TTL.
    /// Overwrites any prior code.
    /// </summary>
    Task IssueAsync(
        Guid userId,
        string code,
        string emailAddress,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the stored code + bound email address, or <c>null</c> when no
    /// active code exists. Does not delete the entry — deletion is atomic on
    /// successful consume via <see cref="ConsumeAsync"/>.
    /// </summary>
    Task<EmailVerificationCodeEntry?> PeekAsync(
        Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically consumes the stored code — the entry is deleted whether or
    /// not <paramref name="candidate"/> matches, so a wrong guess burns the
    /// issued code (T31 / T32 pattern).
    /// </summary>
    Task<EmailVerificationCodeEntry?> ConsumeAsync(
        Guid userId, string candidate, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the remaining cooldown if <c>send-verification</c> was invoked
    /// recently, else <c>null</c>. When <c>null</c> the caller may issue a new
    /// code and must call <see cref="RecordSendAsync"/> to start the cooldown.
    /// </summary>
    Task<TimeSpan?> GetCooldownAsync(
        Guid userId, CancellationToken cancellationToken);

    /// <summary>Starts a cooldown key for the given TTL.</summary>
    Task RecordSendAsync(
        Guid userId, TimeSpan cooldown, CancellationToken cancellationToken);
}

public sealed record EmailVerificationCodeEntry(string Code, string EmailAddress);
