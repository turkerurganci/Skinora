using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// WP16 — translates a <see cref="PlatformOutageAlertEvent"/> (health probe —
/// 05 §4.4, 02 §3.3) into an
/// <see cref="NotificationType.ADMIN_PLATFORM_OUTAGE"/> in-app notification for
/// every admin. A Steam / blockchain sidecar crossed the outage threshold (or
/// recovered), so a durable admin-inbox record sits beside the transient
/// realtime banner and the <c>PLATFORM_OUTAGE_DETECTED</c> audit row. The probe
/// has already frozen the affected timeouts by the time this arrives (backlog
/// WP1/T50, 02 §3.3); the admin can still apply a manual maintenance freeze (WP7).
/// </summary>
public sealed class PlatformOutageAdminNotificationConsumer
    : AdminBroadcastNotificationConsumerBase<PlatformOutageAlertEvent>
{
    public PlatformOutageAdminNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        IAdminRecipientResolver adminRecipients,
        ILogger<PlatformOutageAdminNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, adminRecipients, logger)
    {
    }

    protected override string ConsumerName => "notifications.platform-outage-admin";

    protected override AdminNotificationTemplate BuildAdminTemplate(PlatformOutageAlertEvent domainEvent)
        => new(
            Type: NotificationType.ADMIN_PLATFORM_OUTAGE,
            Parameters: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Component"] = domainEvent.Component,
                ["Status"] = domainEvent.Status,
            });
}
