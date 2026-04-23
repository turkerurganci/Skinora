using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Settings;

public sealed class EmailVerificationService : IEmailVerificationService
{
    // 07 §5.7 — expiresIn = 600s / cooldown prevents resend spam.
    internal static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan SendCooldown = TimeSpan.FromMinutes(1);

    private readonly AppDbContext _db;
    private readonly IEmailVerificationCodeStore _codes;
    private readonly IEmailSender _sender;
    private readonly INotificationPreferenceStore _preferences;
    private readonly TimeProvider _clock;

    public EmailVerificationService(
        AppDbContext db,
        IEmailVerificationCodeStore codes,
        IEmailSender sender,
        INotificationPreferenceStore preferences,
        TimeProvider clock)
    {
        _db = db;
        _codes = codes;
        _sender = sender;
        _preferences = preferences;
        _clock = clock;
    }

    public async Task<EmailVerificationSendResult> SendAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Id == userId && !u.IsDeactivated, cancellationToken);

        if (user is null)
            return new EmailVerificationSendResult(
                EmailVerificationSendStatus.UserNotFound, null, 0, 0);

        if (string.IsNullOrWhiteSpace(user.Email))
            return new EmailVerificationSendResult(
                EmailVerificationSendStatus.NoEmailSet, null, 0, 0);

        var cooldown = await _codes.GetCooldownAsync(userId, cancellationToken);
        if (cooldown is { } remaining && remaining > TimeSpan.Zero)
            return new EmailVerificationSendResult(
                EmailVerificationSendStatus.Cooldown,
                LoggingEmailSender.MaskAddress(user.Email),
                (int)CodeLifetime.TotalSeconds,
                (int)Math.Ceiling(remaining.TotalSeconds));

        var code = GenerateSixDigitCode();
        await _codes.IssueAsync(userId, code, user.Email, CodeLifetime, cancellationToken);
        await _codes.RecordSendAsync(userId, SendCooldown, cancellationToken);
        await _sender.SendVerificationCodeAsync(user.Email, code, CodeLifetime, cancellationToken);

        return new EmailVerificationSendResult(
            EmailVerificationSendStatus.Sent,
            LoggingEmailSender.MaskAddress(user.Email),
            (int)CodeLifetime.TotalSeconds,
            0);
    }

    public async Task<EmailVerifyResult> VerifyAsync(
        Guid userId, string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new EmailVerifyResult(EmailVerifyStatus.InvalidCode, null);

        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(
                u => u.Id == userId && !u.IsDeactivated, cancellationToken);

        if (user is null)
            return new EmailVerifyResult(EmailVerifyStatus.UserNotFound, null);

        if (string.IsNullOrWhiteSpace(user.Email))
            return new EmailVerifyResult(EmailVerifyStatus.NoEmailSet, null);

        // Peek first so we can distinguish "no pending code" (expired/never
        // issued) from "wrong guess" — the former does not burn the stored
        // code on the next call; the latter does.
        var pending = await _codes.PeekAsync(userId, cancellationToken);
        if (pending is null)
            return new EmailVerifyResult(EmailVerifyStatus.CodeExpired, null);

        var consumed = await _codes.ConsumeAsync(userId, code, cancellationToken);
        if (consumed is null)
            return new EmailVerifyResult(EmailVerifyStatus.InvalidCode, null);

        if (!string.Equals(consumed.EmailAddress, user.Email, StringComparison.OrdinalIgnoreCase))
            return new EmailVerifyResult(EmailVerifyStatus.CodeExpired, null);

        var now = _clock.GetUtcNow().UtcDateTime;
        user.EmailVerifiedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        await _preferences.UpsertPreferenceAsync(
            userId,
            NotificationChannel.EMAIL,
            externalId: user.Email,
            isEnabled: true,
            verifiedAt: now,
            cancellationToken);

        return new EmailVerifyResult(EmailVerifyStatus.Verified, now);
    }

    private static string GenerateSixDigitCode()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt32(bytes) % 1_000_000u;
        return value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
