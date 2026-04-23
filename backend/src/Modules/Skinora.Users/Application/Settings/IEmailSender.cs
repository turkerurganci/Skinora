namespace Skinora.Users.Application.Settings;

/// <summary>
/// Abstraction over transactional email delivery. The real provider (Resend)
/// arrives with T78 in the integrations phase (08 §5.1); until then
/// <see cref="LoggingEmailSender"/> captures the call and logs it so test
/// rigs can observe verification codes without a network dependency.
/// </summary>
public interface IEmailSender
{
    Task SendVerificationCodeAsync(
        string toAddress,
        string verificationCode,
        TimeSpan validFor,
        CancellationToken cancellationToken);
}
