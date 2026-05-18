using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Infrastructure.Channels;

/// <summary>
/// Default T37 stub for the Discord channel — logs the rendered template
/// and returns success. Active whenever <c>Discord:Provider</c> is not
/// <c>discord</c> (CI, dev, staging without a bot token), so the
/// dispatcher pipeline stays runnable without contacting the Discord
/// API. T80 replaces this with
/// <see cref="DiscordNotificationChannelHandler"/> at composition time
/// based on the provider switch.
/// </summary>
public sealed class LoggingDiscordNotificationChannelHandler : INotificationChannelHandler
{
    private readonly ILogger<LoggingDiscordNotificationChannelHandler> _logger;

    public LoggingDiscordNotificationChannelHandler(
        ILogger<LoggingDiscordNotificationChannelHandler> logger)
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
            TargetExternalIdMasker.Mask(Channel, targetExternalId),
            rendered.Title);

        return Task.CompletedTask;
    }
}
