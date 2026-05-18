using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Email;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Settings;

/// <summary>
/// Production <see cref="IEmailSender"/> that delivers the email
/// verification code through Resend (T78 — 08 §4.2). Wraps the shared
/// <see cref="IResendEmailClient"/> with localized subject / body copy
/// (08 §4.2 "Hesap") and the standard HTML wrapper so verification
/// emails carry the same chrome as the rest of the account-category
/// transactional fleet.
/// </summary>
/// <remarks>
/// <para>
/// Replaces <see cref="LoggingEmailSender"/> when
/// <c>Resend:Provider</c> is <c>resend</c>; the logging stub stays in
/// the DI graph so CI + integration tests keep working without an
/// outbound HTTP dependency. Permanent Resend failures
/// (<c>422</c> validation, <c>401</c> auth) bubble up so the caller
/// (<see cref="EmailVerificationService"/>) maps them to a user-visible
/// error; transient failures bubble up identically — Resend's webhook
/// pipeline (08 §4.3) is the audit trail for delivery state and there
/// is no notification-row equivalent for the synchronous verification
/// path to defer onto.
/// </para>
/// </remarks>
public sealed class ResendVerificationEmailSender : IEmailSender
{
    private readonly AppDbContext _db;
    private readonly IResendEmailClient _resendClient;
    private readonly IEmailHtmlRenderer _htmlRenderer;
    private readonly ILogger<ResendVerificationEmailSender> _logger;

    public ResendVerificationEmailSender(
        AppDbContext db,
        IResendEmailClient resendClient,
        IEmailHtmlRenderer htmlRenderer,
        ILogger<ResendVerificationEmailSender> logger)
    {
        _db = db;
        _resendClient = resendClient;
        _htmlRenderer = htmlRenderer;
        _logger = logger;
    }

    public async Task SendVerificationCodeAsync(
        string toAddress,
        string verificationCode,
        TimeSpan validFor,
        CancellationToken cancellationToken)
    {
        var locale = await ResolveLocaleAsync(toAddress, cancellationToken);
        var minutes = Math.Max(1, (int)Math.Round(validFor.TotalMinutes));
        var copy = ResolveCopy(locale, verificationCode, minutes);

        var html = _htmlRenderer.Render(EmailCategory.Account, locale, copy.Title, copy.Body);

        var request = new ResendSendEmailRequest(
            ToAddress: toAddress,
            Subject: copy.Title,
            HtmlBody: html.Html,
            TextBody: html.Text,
            Tags: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["category"] = "account",
                ["intent"] = "email_verification",
            });

        var result = await _resendClient.SendAsync(request, cancellationToken);

        _logger.LogInformation(
            "Verification email dispatched via Resend — recipient={Masked} messageId={MessageId} ttlMinutes={Minutes}",
            LoggingEmailSender.MaskAddress(toAddress),
            result.MessageId,
            minutes);
    }

    private async Task<string> ResolveLocaleAsync(string email, CancellationToken cancellationToken)
    {
        var locale = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Email == email && !u.IsDeactivated)
            .Select(u => u.PreferredLanguage)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(locale) ? "en" : locale;
    }

    private static (string Title, string Body) ResolveCopy(string locale, string code, int validMinutes)
    {
        var twoLetter = locale.Length >= 2 ? locale[..2].ToLowerInvariant() : locale.ToLowerInvariant();
        return twoLetter switch
        {
            "tr" => (
                "Skinora e-postanızı doğrulayın",
                $"Doğrulama kodunuz: {code}\nBu kod {validMinutes} dakika boyunca geçerlidir. Talebi siz başlatmadıysanız bu mesajı yok sayabilirsiniz."),
            "es" => (
                "Verifica tu correo de Skinora",
                $"Tu código de verificación: {code}\nEste código es válido durante {validMinutes} minutos. Si no solicitaste este código, ignora este mensaje."),
            "zh" => (
                "验证您的 Skinora 邮箱",
                $"您的验证码：{code}\n该验证码在 {validMinutes} 分钟内有效。如果不是您本人操作，请忽略此邮件。"),
            _ => (
                "Verify your Skinora email",
                $"Your verification code is: {code}\nThis code is valid for {validMinutes} minutes. If you did not request it, you can safely ignore this message."),
        };
    }
}
