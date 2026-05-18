using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Infrastructure.Channels;

/// <summary>
/// Default T37 stub for the Telegram channel — logs the rendered template
/// and returns success. Active whenever <c>Telegram:Provider</c> is not
/// <c>telegram</c> (CI, dev, staging without a bot token), so the
/// dispatcher pipeline stays runnable without contacting the Telegram
/// Bot API. T79 replaces this with
/// <see cref="TelegramNotificationChannelHandler"/> at composition time
/// based on the provider switch.
/// </summary>
public sealed class LoggingTelegramNotificationChannelHandler : INotificationChannelHandler
{
    private readonly ILogger<LoggingTelegramNotificationChannelHandler> _logger;

    public LoggingTelegramNotificationChannelHandler(
        ILogger<LoggingTelegramNotificationChannelHandler> logger)
    {
        _logger = logger;
    }

    public NotificationChannel Channel => NotificationChannel.TELEGRAM;

    public Task SendAsync(
        string targetExternalId,
        RenderedNotificationTemplate rendered,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[T37 stub] Telegram channel send → chat={ChatId} title={Title}",
            TargetExternalIdMasker.Mask(Channel, targetExternalId),
            rendered.Title);

        return Task.CompletedTask;
    }
}
