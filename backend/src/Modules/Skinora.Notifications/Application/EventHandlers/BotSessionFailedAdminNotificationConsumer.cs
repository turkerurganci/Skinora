using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// WP8 — translates a <see cref="BotSessionFailedEvent"/> (Steam webhook
/// handler — 02 §15, 05 §3.2, 08 §3.3) into an
/// <see cref="NotificationType.ADMIN_STEAM_BOT_ISSUE"/> in-app notification for
/// every admin. A platform Steam account dropped out of the active pool, so a
/// durable admin-inbox record sits beside the transient realtime banner and the
/// <c>BOT_SESSION_FAILED</c> audit row.
/// </summary>
public sealed class BotSessionFailedAdminNotificationConsumer
    : AdminBroadcastNotificationConsumerBase<BotSessionFailedEvent>
{
    public BotSessionFailedAdminNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        IAdminRecipientResolver adminRecipients,
        ILogger<BotSessionFailedAdminNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, adminRecipients, logger)
    {
    }

    protected override string ConsumerName => "notifications.bot-session-failed-admin";

    protected override AdminNotificationTemplate BuildAdminTemplate(BotSessionFailedEvent domainEvent)
    {
        var issue = string.IsNullOrWhiteSpace(domainEvent.Reason)
            ? domainEvent.NewStatus
            : $"{domainEvent.NewStatus} ({domainEvent.Reason})";

        return new AdminNotificationTemplate(
            Type: NotificationType.ADMIN_STEAM_BOT_ISSUE,
            Parameters: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BotId"] = domainEvent.DisplayName,
                ["Issue"] = issue,
            });
    }
}
