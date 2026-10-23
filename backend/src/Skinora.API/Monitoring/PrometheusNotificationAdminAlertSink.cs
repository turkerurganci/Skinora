using Prometheus;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Domain.Entities;
using Skinora.Notifications.Infrastructure.Channels;

namespace Skinora.API.Monitoring;

/// <summary>
/// T165 — the "all retries exhausted" escape hatch (05 §7.5) made visible outside
/// the log: besides the T37 warning line it publishes the Unix time of the latest
/// exhausted delivery per channel, read by the <c>notification-delivery-exhausted</c>
/// alert. A revoked Telegram token or a Resend key that lost its domain shows up
/// here, not only as FAILED rows nobody looks at.
/// </summary>
/// <remarks>A timestamp, not a counter, for the reason given on <see cref="PrometheusAdminMoneyAlarmObserver"/>.</remarks>
public sealed class PrometheusNotificationAdminAlertSink : INotificationAdminAlertSink
{
    public static readonly Gauge LastExhausted = Metrics.CreateGauge(
        "skinora_backend_notification_delivery_exhausted_last_seconds",
        "Unix time of the latest notification delivery that exhausted its retries, per channel",
        new GaugeConfiguration { LabelNames = ["channel"] });

    private readonly LoggingNotificationAdminAlertSink _log;
    private readonly TimeProvider _clock;

    public PrometheusNotificationAdminAlertSink(
        ILogger<LoggingNotificationAdminAlertSink> logger,
        TimeProvider clock)
    {
        _log = new LoggingNotificationAdminAlertSink(logger);
        _clock = clock;
    }

    public async Task RaiseDeliveryExhaustedAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        await _log.RaiseDeliveryExhaustedAsync(delivery, cancellationToken);
        LastExhausted.WithLabels(delivery.Channel.ToString())
            .Set(_clock.GetUtcNow().ToUnixTimeMilliseconds() / 1000d);
    }
}
