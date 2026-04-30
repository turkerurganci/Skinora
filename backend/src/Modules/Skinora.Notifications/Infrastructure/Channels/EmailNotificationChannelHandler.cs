using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Infrastructure.Channels;

/// <summary>
/// T37 placeholder Email channel handler — logs the rendered template and
/// returns success without contacting any external service. T78 (Resend
/// entegrasyonu) replaces this implementation via DI; the
/// <see cref="INotificationChannelHandler"/> contract is the swap point.
/// </summary>
public sealed class EmailNotificationChannelHandler : INotificationChannelHandler
{
    private readonly ILogger<EmailNotificationChannelHandler> _logger;

    public EmailNotificationChannelHandler(ILogger<EmailNotificationChannelHandler> logger)
    {
        _logger = logger;
    }

    public NotificationChannel Channel => NotificationChannel.EMAIL;

    public Task SendAsync(
        string targetExternalId,
        RenderedNotificationTemplate rendered,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[T37 stub] Email channel send → target={Target} title={Title}",
            TargetExternalIdMasker.Mask(Channel, targetExternalId),
            rendered.Title);

        return Task.CompletedTask;
    }
}
