using Prometheus;
using Skinora.Shared.Notifications;

namespace Skinora.API.Monitoring;

/// <summary>
/// T165 — announces, once at startup, what every external notification channel
/// does in this deployment (<see cref="INotificationChannelAvailability"/>) and
/// publishes it as <c>skinora_backend_notification_channel_state{channel,state}</c>
/// (1 for the current state, 0 for the others).
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="NotificationChannelState.Misconfigured"/> channel — the real
/// provider selected with a missing, placeholder or malformed secret — is an
/// <b>error</b> line naming the configuration keys (never their values) and fires
/// the <c>notification-channel-misconfigured</c> alert. The platform keeps running
/// (fail-open for the site, fail-closed for the channel: nothing is sent on it).
/// </para>
/// <para>
/// The development stub in a non-Development environment is a warning: messages
/// on that channel are only written to the log while the UI lets users connect it.
/// The trial overlay therefore never leaves a channel on <c>logging</c>.
/// </para>
/// </remarks>
public sealed class NotificationChannelStatusReporter : IHostedService
{
    public static readonly Gauge ChannelState = Metrics.CreateGauge(
        "skinora_backend_notification_channel_state",
        "External notification channel state, 1 for the current state (stub/live/disabled/misconfigured)",
        new GaugeConfiguration { LabelNames = ["channel", "state"] });

    private readonly INotificationChannelAvailability _channels;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<NotificationChannelStatusReporter> _logger;

    public NotificationChannelStatusReporter(
        INotificationChannelAvailability channels,
        IHostEnvironment environment,
        ILogger<NotificationChannelStatusReporter> logger)
    {
        _channels = channels;
        _environment = environment;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Report(_channels, _environment.IsDevelopment(), _logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Logs and publishes every channel's state. Separate from the host for tests.</summary>
    public static void Report(INotificationChannelAvailability channels, bool isDevelopment, ILogger logger)
    {
        foreach (var status in channels.All)
        {
            var channel = status.Channel.ToString();
            foreach (var state in Enum.GetValues<NotificationChannelState>())
            {
                ChannelState.WithLabels(channel, state.ToString().ToLowerInvariant())
                    .Set(state == status.State ? 1 : 0);
            }

            switch (status.State)
            {
                case NotificationChannelState.Live:
                    logger.LogInformation(
                        "Notification channel {Channel}: LIVE (provider {Provider}).", channel, status.Provider);
                    break;
                case NotificationChannelState.Disabled:
                    logger.LogInformation(
                        "Notification channel {Channel}: DISABLED (provider {Provider}) — not offered to users, nothing sent.",
                        channel, status.Provider);
                    break;
                case NotificationChannelState.Misconfigured:
                    logger.LogError(
                        "Notification channel {Channel}: MISCONFIGURED (provider {Provider}) — treated as switched off until fixed: {Problems}. Fill the secrets and restart the backend (DEPLOY_RUNBOOK §P).",
                        channel, status.Provider, string.Join(", ", status.Problems));
                    break;
                case NotificationChannelState.Stub when !isDevelopment:
                    logger.LogWarning(
                        "Notification channel {Channel}: development stub (provider {Provider}) outside Development — messages are only logged.",
                        channel, status.Provider);
                    break;
                default:
                    logger.LogInformation(
                        "Notification channel {Channel}: stub (provider {Provider}).", channel, status.Provider);
                    break;
            }
        }
    }
}
