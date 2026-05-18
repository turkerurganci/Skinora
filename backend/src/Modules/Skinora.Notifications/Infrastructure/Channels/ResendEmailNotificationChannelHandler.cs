using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Domain.Entities;
using Skinora.Notifications.Infrastructure.Email;
using Skinora.Shared.Email;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Notifications.Infrastructure.Channels;

/// <summary>
/// Resend-backed <see cref="INotificationChannelHandler"/> for the
/// <see cref="NotificationChannel.EMAIL"/> channel (T78 — 08 §4.1–§4.3).
/// Replaces <see cref="EmailNotificationChannelHandler"/> when the
/// <c>Resend:Provider</c> setting is <c>resend</c>; the stub stays
/// registered for tests and development.
/// </summary>
/// <remarks>
/// <para>
/// Resolves the recipient locale via the originating
/// <see cref="Notification.UserId"/> so the HTML banner / footer chrome
/// matches the localized title + body that the dispatcher already
/// rendered. Translates Resend transport exceptions into the channel
/// abstractions:
/// </para>
/// <list type="bullet">
///   <item><see cref="ResendPermanentException"/> →
///         <see cref="PermanentChannelDeliveryException"/> (no retry,
///         FAILED + admin alert)</item>
///   <item><see cref="ResendTransientException"/> →
///         <see cref="TransientChannelDeliveryException"/> (immediate +
///         deferred retry tiers)</item>
/// </list>
/// </remarks>
public sealed class ResendEmailNotificationChannelHandler : INotificationChannelHandler
{
    private const string DefaultLocale = "en";

    private readonly AppDbContext _dbContext;
    private readonly IResendEmailClient _resendClient;
    private readonly IEmailHtmlRenderer _htmlRenderer;
    private readonly ILogger<ResendEmailNotificationChannelHandler> _logger;

    public ResendEmailNotificationChannelHandler(
        AppDbContext dbContext,
        IResendEmailClient resendClient,
        IEmailHtmlRenderer htmlRenderer,
        ILogger<ResendEmailNotificationChannelHandler> logger)
    {
        _dbContext = dbContext;
        _resendClient = resendClient;
        _htmlRenderer = htmlRenderer;
        _logger = logger;
    }

    public NotificationChannel Channel => NotificationChannel.EMAIL;

    public async Task SendAsync(
        string targetExternalId,
        RenderedNotificationTemplate rendered,
        CancellationToken cancellationToken)
    {
        var (locale, category) = await ResolveContextAsync(targetExternalId, cancellationToken);
        var html = _htmlRenderer.Render(category, locale, rendered.Title, rendered.Body);

        var request = new ResendSendEmailRequest(
            ToAddress: targetExternalId,
            Subject: rendered.Title,
            HtmlBody: html.Html,
            TextBody: html.Text,
            Tags: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["category"] = category.ToString().ToLowerInvariant(),
            });

        try
        {
            var result = await _resendClient.SendAsync(request, cancellationToken);

            _logger.LogInformation(
                "Resend email accepted — target={Target} category={Category} messageId={MessageId}",
                TargetExternalIdMasker.Mask(Channel, targetExternalId),
                category,
                result.MessageId);
        }
        catch (ResendPermanentException ex)
        {
            throw new PermanentChannelDeliveryException(
                $"Resend rejected email permanently ({ex.HttpStatusCode}/{ex.ResendErrorName}): {ex.Message}",
                ex);
        }
        catch (ResendTransientException ex)
        {
            throw new TransientChannelDeliveryException(
                $"Resend send failed transiently ({ex.HttpStatusCode}/{ex.ResendErrorName}): {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Loads the recipient's preferred locale and the email category for
    /// the originating notification. Looking the row up by target email
    /// avoids piping the user id / notification type through the channel
    /// abstraction, which would force the same shape onto Telegram /
    /// Discord that have no equivalent context.
    /// </summary>
    private async Task<(string Locale, EmailCategory Category)> ResolveContextAsync(
        string targetEmail,
        CancellationToken cancellationToken)
    {
        // The freshest pending delivery for this email + EMAIL channel
        // points us at the notification whose locale + type we need. A
        // single dispatcher run inserts the delivery row before enqueuing
        // the job, so the row always exists by the time we get here.
        var match = await _dbContext.Set<NotificationDelivery>()
            .AsNoTracking()
            .Where(d => d.Channel == NotificationChannel.EMAIL && d.TargetExternalId == targetEmail)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new
            {
                d.NotificationId,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null)
        {
            // Defensive — shouldn't happen in production. Fall back to
            // transaction category + English so the email still goes out.
            _logger.LogWarning(
                "No NotificationDelivery row found for target {Target}; defaulting locale + category.",
                TargetExternalIdMasker.Mask(NotificationChannel.EMAIL, targetEmail));
            return (DefaultLocale, EmailCategory.Transaction);
        }

        var lookup = await _dbContext.Set<Notification>()
            .AsNoTracking()
            .Where(n => n.Id == match.NotificationId)
            .Select(n => new { n.UserId, n.Type })
            .FirstOrDefaultAsync(cancellationToken);

        if (lookup is null)
        {
            return (DefaultLocale, EmailCategory.Transaction);
        }

        var locale = await _dbContext.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == lookup.UserId)
            .Select(u => u.PreferredLanguage)
            .FirstOrDefaultAsync(cancellationToken);

        return (
            string.IsNullOrWhiteSpace(locale) ? DefaultLocale : locale,
            EmailCategoryMap.Resolve(lookup.Type));
    }
}
