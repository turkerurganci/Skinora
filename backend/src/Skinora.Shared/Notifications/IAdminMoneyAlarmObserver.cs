namespace Skinora.Shared.Notifications;

/// <summary>
/// T165 — the backend's "para alarmı" (money alarm) category. An admin alert
/// whose consumer is built with this observer is a money alarm: money is stuck,
/// failed or waiting for a person (a payout / refund / sweep that exhausted its
/// retries, a refund that could not be made, a deferred seller payout, a parked
/// settlement holding the buyer's money).
/// </summary>
/// <remarks>
/// <para>
/// The in-app admin notification and each admin's own connected channels
/// (e-mail, Telegram — <c>AdminBroadcastNotificationConsumerBase</c>) are the
/// first path. This observer is the second, independent one: the production
/// implementation publishes <c>skinora_backend_admin_money_alarm_last_seconds{kind}</c>
/// and the Grafana rule <c>admin-money-alarm-raised</c> (label <c>category: money</c>)
/// carries it to the operator's Telegram chat and e-mail even when no admin has
/// connected a channel — or when no admin account exists at all.
/// </para>
/// <para>
/// A new money alarm joins the category by passing this observer to the
/// money-alarm constructor of <c>AdminBroadcastNotificationConsumerBase</c>; its
/// <c>ConsumerName</c> becomes the <c>kind</c> label. Nothing else is needed.
/// </para>
/// </remarks>
public interface IAdminMoneyAlarmObserver
{
    /// <summary>Records that a money alarm of <paramref name="kind"/> was raised now.</summary>
    void Raised(string kind);
}

/// <summary>Default observer (tests, modules composed without the API host).</summary>
public sealed class NullAdminMoneyAlarmObserver : IAdminMoneyAlarmObserver
{
    public static readonly NullAdminMoneyAlarmObserver Instance = new();

    public void Raised(string kind)
    {
    }
}
