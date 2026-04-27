using Skinora.Notifications.Domain.Entities;

namespace Skinora.Notifications.Application.Channels;

/// <summary>
/// Final-failure escape hatch for external channel deliveries (05 §7.5
/// "Tüm kanallar başarısız → Log + admin alert").
/// </summary>
/// <remarks>
/// Invoked by <see cref="DeliveryJobs.NotificationDeliveryJob"/> when a
/// <see cref="NotificationDelivery"/> reaches the configured maximum retry
/// count. The default <c>LoggingNotificationAdminAlertSink</c> writes a
/// structured warning to the application log; a future task can swap it for
/// an AuditLog/Slack/PagerDuty implementation by re-registering the interface.
/// </remarks>
public interface INotificationAdminAlertSink
{
    Task RaiseDeliveryExhaustedAsync(NotificationDelivery delivery, CancellationToken cancellationToken);
}
