using Prometheus;
using Skinora.Shared.Notifications;

namespace Skinora.API.Monitoring;

/// <summary>
/// T165 — publishes the backend's money alarms (<see cref="IAdminMoneyAlarmObserver"/>)
/// as the Unix time of the latest one per kind. The <c>admin-money-alarm-raised</c>
/// rule fires while <c>time() - last</c> is under an hour.
/// </summary>
/// <remarks>
/// A timestamp gauge rather than a counter on purpose: a counter series that
/// appears with its first increment is invisible to <c>increase()</c> (the first
/// sample is the baseline), so the very first alarm of a kind — the one that
/// matters — would never fire without a primer listing every kind in advance.
/// A timestamp needs no primer, so a new money-alarm consumer is covered the
/// moment it passes the observer (T169a and later). A backend restart forgets
/// the timestamp: the alarm resolves early, the in-app notification stays.
/// </remarks>
public sealed class PrometheusAdminMoneyAlarmObserver : IAdminMoneyAlarmObserver
{
    public static readonly Gauge LastRaised = Metrics.CreateGauge(
        "skinora_backend_admin_money_alarm_last_seconds",
        "Unix time of the latest backend money alarm per kind (admin consumer name)",
        new GaugeConfiguration { LabelNames = ["kind"] });

    private readonly TimeProvider _clock;

    public PrometheusAdminMoneyAlarmObserver(TimeProvider clock)
    {
        _clock = clock;
    }

    public void Raised(string kind) =>
        LastRaised.WithLabels(kind).Set(_clock.GetUtcNow().ToUnixTimeMilliseconds() / 1000d);
}
