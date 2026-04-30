using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Infrastructure.Channels;

/// <summary>
/// T37 placeholder Telegram channel handler — logs the rendered template and
/// returns success. T79 swaps in the real Telegram Bot API client via DI.
/// </summary>
public sealed class TelegramNotificationChannelHandler : INotificationChannelHandler
{
    private readonly ILogger<TelegramNotificationChannelHandler> _logger;

    public TelegramNotificationChannelHandler(ILogger<TelegramNotificationChannelHandler> logger)
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
