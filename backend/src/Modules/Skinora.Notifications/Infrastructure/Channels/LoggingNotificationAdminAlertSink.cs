using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Domain.Entities;

namespace Skinora.Notifications.Infrastructure.Channels;

/// <summary>
/// Default <see cref="INotificationAdminAlertSink"/> — writes a structured
/// warning log entry when an external channel delivery exhausts its retry
/// budget (05 §7.5 "Tüm kanallar başarısız → Log + admin alert").
/// </summary>
/// <remarks>
/// Future hardening (post-MVP) can replace this implementation with an
/// AuditLog write or a Slack/PagerDuty bridge by re-registering the
/// interface; <see cref="DeliveryJobs.NotificationDeliveryJob"/> only depends
/// on the abstraction.
/// </remarks>
public sealed class LoggingNotificationAdminAlertSink : INotificationAdminAlertSink
{
    private readonly ILogger<LoggingNotificationAdminAlertSink> _logger;

    public LoggingNotificationAdminAlertSink(ILogger<LoggingNotificationAdminAlertSink> logger)
    {
        _logger = logger;
    }

    public Task RaiseDeliveryExhaustedAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "[T37 admin-alert] Notification delivery exhausted retries — DeliveryId={DeliveryId} NotificationId={NotificationId} Channel={Channel} Attempts={Attempts} LastError={LastError}",
            delivery.Id,
            delivery.NotificationId,
            delivery.Channel,
            delivery.AttemptCount,
            delivery.LastError);

        return Task.CompletedTask;
    }
}
