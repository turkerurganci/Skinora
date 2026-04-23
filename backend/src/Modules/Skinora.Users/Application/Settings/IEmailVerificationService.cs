namespace Skinora.Users.Application.Settings;

/// <summary>
/// Send + verify flow for email ownership (07 §5.7, §5.8). A single active
/// 6-digit code is kept per user in <see cref="IEmailVerificationCodeStore"/>;
/// a separate cooldown key rate-limits <c>send-verification</c>.
/// </summary>
public interface IEmailVerificationService
{
    Task<EmailVerificationSendResult> SendAsync(
        Guid userId, CancellationToken cancellationToken);

    Task<EmailVerifyResult> VerifyAsync(
        Guid userId, string? code, CancellationToken cancellationToken);
}

public enum EmailVerificationSendStatus
{
    Sent,
    NoEmailSet,
    Cooldown,
    UserNotFound,
}

public sealed record EmailVerificationSendResult(
    EmailVerificationSendStatus Status,
    string? MaskedAddress,
    int ExpiresInSeconds,
    int RetryAfterSeconds);

public enum EmailVerifyStatus
{
    Verified,
    NoEmailSet,
    CodeExpired,
    InvalidCode,
    UserNotFound,
}

public sealed record EmailVerifyResult(
    EmailVerifyStatus Status,
    DateTime? VerifiedAt);
