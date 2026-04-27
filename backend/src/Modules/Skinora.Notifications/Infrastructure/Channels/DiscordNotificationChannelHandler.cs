using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Infrastructure.Channels;

/// <summary>
/// T37 placeholder Discord channel handler — logs the rendered template and
/// returns success. T80 swaps in the real Discord webhook/Bot API client via
/// DI.
/// </summary>
public sealed class DiscordNotificationChannelHandler : INotificationChannelHandler
{
    private readonly ILogger<DiscordNotificationChannelHandler> _logger;

    public DiscordNotificationChannelHandler(ILogger<DiscordNotificationChannelHandler> logger)
    {
        _logger = logger;
    }

    public NotificationChannel Channel => NotificationChannel.DISCORD;

    public Task SendAsync(
        string targetExternalId,
        RenderedNotificationTemplate rendered,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[T37 stub] Discord channel send → user={UserId} title={Title}",
            targetExternalId,
            rendered.Title);

        return Task.CompletedTask;
    }
}
