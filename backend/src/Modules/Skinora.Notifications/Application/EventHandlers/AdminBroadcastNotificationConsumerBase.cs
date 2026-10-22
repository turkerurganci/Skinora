using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Notifications;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Base class for WP8 admin-alert consumers that fan a single admin
/// notification out to <b>every</b> admin (06 §2.13 "Admin" target types).
/// Subclasses only describe the notification body via
/// <see cref="BuildAdminTemplate"/>; this base resolves the recipient set
/// through <see cref="IAdminRecipientResolver"/> and emits one
/// <see cref="NotificationRequest"/> per admin.
/// </summary>
/// <typeparam name="TEvent">Concrete domain event the derived class consumes.</typeparam>
/// <remarks>
/// Idempotency is inherited from <see cref="NotificationConsumerBase{TEvent}"/>
/// — the event is marked processed once per consumer name, so a redelivered
/// outbox row never double-notifies, even if the admin set changed between
/// deliveries. When no admins are resolved the consumer logs a warning and
/// no-ops (the alert is still in the audit trail / Loki logs).
/// </remarks>
public abstract class AdminBroadcastNotificationConsumerBase<TEvent>
    : NotificationConsumerBase<TEvent>
    where TEvent : IDomainEvent
{
    private readonly IAdminRecipientResolver _adminRecipients;
    private readonly IAdminMoneyAlarmObserver? _moneyAlarms;
    private readonly ILogger _logger;

    protected AdminBroadcastNotificationConsumerBase(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        IAdminRecipientResolver adminRecipients,
        ILogger logger)
        : base(dispatcher, processedEventStore, logger)
    {
        _adminRecipients = adminRecipients;
        _logger = logger;
    }

    /// <summary>
    /// T165 — money-alarm constructor ("para alarmı", <see cref="IAdminMoneyAlarmObserver"/>):
    /// besides the in-app + per-admin channel fan-out, every event is reported to
    /// <paramref name="moneyAlarms"/> under <see cref="NotificationConsumerBase{TEvent}.ConsumerName"/>,
    /// which the <c>admin-money-alarm-raised</c> Grafana rule routes to the
    /// operator's Telegram chat and e-mail — also when no admin is resolved.
    /// </summary>
    protected AdminBroadcastNotificationConsumerBase(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        IAdminRecipientResolver adminRecipients,
        IAdminMoneyAlarmObserver moneyAlarms,
        ILogger logger)
        : this(dispatcher, processedEventStore, adminRecipients, logger)
    {
        _moneyAlarms = moneyAlarms;
    }

    /// <summary>True when this consumer is in the money-alarm category.</summary>
    public bool IsMoneyAlarm => _moneyAlarms is not null;

    /// <summary>
    /// Describes the notification fanned out to every admin (type, template
    /// parameters and optional transaction/flag targets). Called once per
    /// event; the recipient <c>UserId</c> is filled in per admin by the base.
    /// </summary>
    protected abstract AdminNotificationTemplate BuildAdminTemplate(TEvent domainEvent);

    protected override async Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        TEvent domainEvent,
        CancellationToken cancellationToken)
    {
        // T165 — the money-alarm path does not depend on admins existing or
        // having connected a channel; report before resolving recipients.
        _moneyAlarms?.Raised(ConsumerName);

        var adminUserIds = await _adminRecipients.GetAdminUserIdsAsync(cancellationToken);
        if (adminUserIds.Count == 0)
        {
            _logger.LogWarning(
                "{Consumer}: no admin recipients resolved for event {EventId}; in-app admin alert skipped.",
                ConsumerName,
                domainEvent.EventId);
            return Array.Empty<NotificationRequest>();
        }

        var template = BuildAdminTemplate(domainEvent);

        return adminUserIds
            .Select(adminUserId => new NotificationRequest
            {
                UserId = adminUserId,
                Type = template.Type,
                TransactionId = template.TransactionId,
                FlagId = template.FlagId,
                Parameters = template.Parameters,
            })
            .ToList();
    }
}

/// <summary>
/// Immutable description of an admin notification body — everything except the
/// recipient, which <see cref="AdminBroadcastNotificationConsumerBase{TEvent}"/>
/// supplies per admin.
/// </summary>
/// <param name="Type">Admin <see cref="NotificationType"/> to dispatch.</param>
/// <param name="Parameters">Template placeholder substitutions.</param>
/// <param name="TransactionId">Related transaction (inbox "transaction" target), if any.</param>
/// <param name="FlagId">Related fraud flag (inbox "flag" target for ADMIN_FLAG_ALERT), if any.</param>
public sealed record AdminNotificationTemplate(
    NotificationType Type,
    IReadOnlyDictionary<string, string> Parameters,
    Guid? TransactionId = null,
    Guid? FlagId = null);
