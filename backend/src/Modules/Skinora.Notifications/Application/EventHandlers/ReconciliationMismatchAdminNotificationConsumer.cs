using System.Globalization;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// T165 — translates a <see cref="ReconciliationMismatchAdminAlertEvent"/> (daily
/// ledger reconciliation, 05 §3.3 "Uyuşmazlık tespit edilirse admin'e critical
/// alert") into an <see cref="NotificationType.ADMIN_RECONCILIATION_MISMATCH"/>
/// notification for every admin: the inbox, and each admin's own connected
/// e-mail / Telegram. Before T165 a mismatch reached only an open admin panel.
/// </summary>
/// <remarks>
/// Built with the plain (non money-alarm) constructor on purpose: the operator's
/// Telegram chat and e-mail already get the dedicated <c>reconciliation-mismatch</c>
/// money alarm from <c>skinora_backend_reconciliation_mismatches</c>, which also
/// says "resolved" when the books match again. Passing the money-alarm observer
/// here would announce the same mismatch a second time as
/// <c>admin-money-alarm-raised</c>.
/// </remarks>
public sealed class ReconciliationMismatchAdminNotificationConsumer
    : AdminBroadcastNotificationConsumerBase<ReconciliationMismatchAdminAlertEvent>
{
    public ReconciliationMismatchAdminNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        IAdminRecipientResolver adminRecipients,
        ILogger<ReconciliationMismatchAdminNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, adminRecipients, logger)
    {
    }

    protected override string ConsumerName => "notifications.reconciliation-mismatch-admin";

    protected override AdminNotificationTemplate BuildAdminTemplate(ReconciliationMismatchAdminAlertEvent domainEvent)
        => new(
            Type: NotificationType.ADMIN_RECONCILIATION_MISMATCH,
            Parameters: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Network"] = domainEvent.Network,
                ["MismatchCount"] = domainEvent.MismatchCount.ToString(CultureInfo.InvariantCulture),
            });
}
