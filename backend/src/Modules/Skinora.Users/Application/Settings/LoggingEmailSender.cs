using Microsoft.Extensions.Logging;

namespace Skinora.Users.Application.Settings;

/// <summary>
/// Development-safe stub: logs what would be sent and returns immediately.
/// The code itself is intentionally *not* logged (would leak a secret into
/// retained logs); only the fact that a send occurred plus a masked recipient
/// is recorded. Swapped for a real Resend-backed implementation by T78.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendVerificationCodeAsync(
        string toAddress,
        string verificationCode,
        TimeSpan validFor,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Email verification code issued (stub) — recipient={MaskedRecipient}, validForSeconds={TtlSeconds}",
            MaskAddress(toAddress),
            (int)validFor.TotalSeconds);
        return Task.CompletedTask;
    }

    internal static string MaskAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "***";
        var at = address.IndexOf('@', StringComparison.Ordinal);
        if (at <= 1) return $"***{address[at..]}";
        return $"{address[0]}***{address[at..]}";
    }
}
