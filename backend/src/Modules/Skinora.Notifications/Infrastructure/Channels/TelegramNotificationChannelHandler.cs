using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Telegram;

namespace Skinora.Notifications.Infrastructure.Channels;

/// <summary>
/// Telegram-backed <see cref="INotificationChannelHandler"/> for the
/// <see cref="NotificationChannel.TELEGRAM"/> channel (T79 — 08 §5.1–§5.5).
/// Replaces the T37 logging stub when the <c>Telegram:Provider</c>
/// setting is <c>telegram</c>; the stub stays registered for tests and
/// development.
/// </summary>
/// <remarks>
/// <para>
/// Responsibilities:
/// </para>
/// <list type="number">
///   <item>Wait on the per-chat + global rate gate (08 §5.3).</item>
///   <item>Escape title + body via <see cref="MarkdownV2Escaper"/> and
///         compose the <c>*title*\n\nbody</c> message envelope.</item>
///   <item>Call <see cref="ITelegramBotClient.SendMessageAsync"/>.</item>
///   <item>Translate Telegram-specific exceptions into the channel
///         abstractions and auto-disable the preference on any
///         documented 403 reason (08 §5.4).</item>
/// </list>
/// </remarks>
public sealed class TelegramNotificationChannelHandler : INotificationChannelHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly ITelegramRateLimiter _rateLimiter;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<TelegramNotificationChannelHandler> _logger;

    public TelegramNotificationChannelHandler(
        ITelegramBotClient botClient,
        ITelegramRateLimiter rateLimiter,
        AppDbContext dbContext,
        ILogger<TelegramNotificationChannelHandler> logger)
    {
        _botClient = botClient;
        _rateLimiter = rateLimiter;
        _dbContext = dbContext;
        _logger = logger;
    }

    public NotificationChannel Channel => NotificationChannel.TELEGRAM;

    public async Task SendAsync(
        string targetExternalId,
        RenderedNotificationTemplate rendered,
        CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitAsync(targetExternalId, cancellationToken);

        var text = FormatMessage(rendered);
        var request = new TelegramSendMessageRequest(
            ChatId: targetExternalId,
            Text: text);

        try
        {
            var result = await _botClient.SendMessageAsync(request, cancellationToken);

            _logger.LogInformation(
                "Telegram message accepted — target={Target} messageId={MessageId}",
                TargetExternalIdMasker.Mask(Channel, targetExternalId),
                result.MessageId);
        }
        catch (TelegramTransientException ex)
        {
            if (ex.RetryAfterSeconds is { } retry && retry > 0)
            {
                _rateLimiter.RegisterRetryAfter(targetExternalId, retry);
            }

            throw new TransientChannelDeliveryException(
                $"Telegram send failed transiently ({ex.HttpStatusCode}/{ex.TelegramErrorCode}): {ex.Message}",
                ex);
        }
        catch (TelegramForbiddenException ex)
        {
            await DisablePreferenceAsync(targetExternalId, ex.Reason, cancellationToken);

            throw new PermanentChannelDeliveryException(
                $"Telegram forbidden ({ex.Reason}/{ex.TelegramErrorDescription}); preference disabled.",
                ex);
        }
        catch (TelegramPermanentException ex)
        {
            // 400 with "chat not found" or similar usually means the user
            // deleted the chat or the chat_id was migrated. Treat as a
            // disconnect: disable the preference so retries don't keep
            // firing into the void.
            await DisablePreferenceAsync(
                targetExternalId,
                TelegramForbiddenReason.Unknown,
                cancellationToken);

            throw new PermanentChannelDeliveryException(
                $"Telegram rejected message permanently ({ex.HttpStatusCode}/{ex.TelegramErrorCode}): {ex.Message}",
                ex);
        }
    }

    private static string FormatMessage(RenderedNotificationTemplate rendered)
    {
        var title = MarkdownV2Escaper.Escape(rendered.Title);
        var body = MarkdownV2Escaper.Escape(rendered.Body);
        return $"*{title}*\n\n{body}";
    }

    private async Task DisablePreferenceAsync(
        string chatId,
        TelegramForbiddenReason reason,
        CancellationToken cancellationToken)
    {
        var preference = await _dbContext.Set<UserNotificationPreference>()
            .Where(p => p.Channel == NotificationChannel.TELEGRAM
                        && p.ExternalId == chatId
                        && !p.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if (preference is null || !preference.IsEnabled)
        {
            return;
        }

        preference.IsEnabled = false;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Telegram preference auto-disabled — target={Target} reason={Reason}",
            TargetExternalIdMasker.Mask(NotificationChannel.TELEGRAM, chatId),
            reason);
    }
}
