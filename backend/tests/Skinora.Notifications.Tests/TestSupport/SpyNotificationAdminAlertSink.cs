using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Domain.Entities;

namespace Skinora.Notifications.Tests.TestSupport;

public sealed class SpyNotificationAdminAlertSink : INotificationAdminAlertSink
{
    public List<NotificationDelivery> Alerts { get; } = new();

    public Task RaiseDeliveryExhaustedAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        Alerts.Add(delivery);
        return Task.CompletedTask;
    }
}
