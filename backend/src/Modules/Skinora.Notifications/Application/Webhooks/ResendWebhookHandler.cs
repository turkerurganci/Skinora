using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Notifications.Application.Webhooks;

/// <summary>
/// Default <see cref="IResendWebhookHandler"/> — applies the action
/// matrix from 08 §4.3 to authenticated Resend events. Disables the
/// EMAIL channel on the relevant <see cref="UserNotificationPreference"/>
/// for bounce / complaint / suppression, logs every event for
/// observability, and surfaces hard failures through the existing
/// admin alert sink shape.
/// </summary>
public sealed class ResendWebhookHandler : IResendWebhookHandler
{
    private const string EmailEventPrefix = "email.";

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ResendWebhookHandler> _logger;

    public ResendWebhookHandler(
        AppDbContext db,
        TimeProvider clock,
        ILogger<ResendWebhookHandler> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ResendWebhookResult> HandleAsync(
        ResendWebhookEnvelope envelope,
        string svixId,
        CancellationToken cancellationToken)
    {
        var eventType = ParseEventType(envelope.Type);
        var recipient = envelope.Data?.To?.FirstOrDefault();
        var emailId = envelope.Data?.EmailId;

        _logger.LogInformation(
            "Resend webhook received — type={Type} svixId={SvixId} emailId={EmailId} recipient={MaskedRecipient}",
            envelope.Type,
            svixId,
            emailId,
            MaskEmail(recipient));

        return eventType switch
        {
            ResendWebhookEventType.Bounced => await DisableChannelAsync(recipient, "bounced", cancellationToken),
            ResendWebhookEventType.Complained => await DisableChannelAsync(recipient, "complained", cancellationToken),
            ResendWebhookEventType.Suppressed => await DisableChannelAsync(recipient, "suppressed", cancellationToken),
            ResendWebhookEventType.Failed => HandleFailed(recipient, emailId),
            ResendWebhookEventType.DeliveryDelayed => HandleDelayed(recipient, emailId),
            _ => ResendWebhookResult.Acknowledged,
        };
    }

    private async Task<ResendWebhookResult> DisableChannelAsync(
        string? recipient,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipient))
        {
            _logger.LogWarning(
                "Resend webhook ({Reason}) carried no recipient; ignoring.",
                reason);
            return ResendWebhookResult.UnknownRecipient;
        }

        var user = await _db.Set<User>()
            .Where(u => u.Email == recipient && !u.IsDeactivated)
            .Select(u => new { u.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            _logger.LogInformation(
                "Resend webhook ({Reason}) recipient {Masked} not found — no preference to disable.",
                reason,
                MaskEmail(recipient));
            return ResendWebhookResult.UnknownRecipient;
        }

        var preference = await _db.Set<UserNotificationPreference>()
            .FirstOrDefaultAsync(
                p => p.UserId == user.Id && p.Channel == NotificationChannel.EMAIL,
                cancellationToken);

        if (preference is null || !preference.IsEnabled)
        {
            _logger.LogInformation(
                "Resend webhook ({Reason}) — EMAIL channel already disabled for user {UserId}.",
                reason,
                user.Id);
            return ResendWebhookResult.Idempotent;
        }

        preference.IsEnabled = false;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Resend webhook ({Reason}) — EMAIL channel disabled for user {UserId}, recipient {Masked} at {AtUtc:O}.",
            reason,
            user.Id,
            MaskEmail(recipient),
            _clock.GetUtcNow().UtcDateTime);

        return ResendWebhookResult.Applied;
    }

    private ResendWebhookResult HandleFailed(string? recipient, string? emailId)
    {
        // Terminal Resend-side failure (provider tried and gave up). We
        // log at warning so the observability stack surfaces an admin
        // alert; the per-row NotificationDelivery FAILED transition is
        // owned by the immediate / deferred delivery pipeline (08 §4.3).
        _logger.LogWarning(
            "Resend webhook (email.failed) — recipient={Masked} emailId={EmailId}. Admin attention required.",
            MaskEmail(recipient),
            emailId);
        return ResendWebhookResult.Acknowledged;
    }

    private ResendWebhookResult HandleDelayed(string? recipient, string? emailId)
    {
        // Informational — Resend will retry on its side. No action; the
        // monitoring stack rolls up repeated delays into an alert
        // (Prometheus/Grafana, T16 dashboards).
        _logger.LogInformation(
            "Resend webhook (email.delivery_delayed) — recipient={Masked} emailId={EmailId}.",
            MaskEmail(recipient),
            emailId);
        return ResendWebhookResult.Acknowledged;
    }

    public static ResendWebhookEventType ParseEventType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ResendWebhookEventType.Unknown;

        var suffix = raw.StartsWith(EmailEventPrefix, StringComparison.OrdinalIgnoreCase)
            ? raw[EmailEventPrefix.Length..]
            : raw;

        return suffix.ToLowerInvariant() switch
        {
            "bounced" => ResendWebhookEventType.Bounced,
            "delivery_delayed" => ResendWebhookEventType.DeliveryDelayed,
            "complained" => ResendWebhookEventType.Complained,
            "failed" => ResendWebhookEventType.Failed,
            "suppressed" => ResendWebhookEventType.Suppressed,
            _ => ResendWebhookEventType.Unknown,
        };
    }

    public static string MaskEmail(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "***";
        var at = address.IndexOf('@', StringComparison.Ordinal);
        if (at <= 1) return $"***{(at >= 0 ? address[at..] : string.Empty)}";
        return $"{address[0]}***{address[at..]}";
    }
}
