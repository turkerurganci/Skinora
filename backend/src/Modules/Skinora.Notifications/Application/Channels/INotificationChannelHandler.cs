using Skinora.Notifications.Application.Templates;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Application.Channels;

/// <summary>
/// Per-channel sender abstraction. Each implementation handles exactly one
/// <see cref="NotificationChannel"/> and is selected by the
/// <see cref="NotificationChannel"/> property (05 §7.2).
/// </summary>
/// <remarks>
/// <para>
/// Throwing an exception signals a transient delivery failure to the Hangfire
/// retry pipeline (<c>NotificationDeliveryJob</c>). Returning normally counts
/// as a successful send (<see cref="Skinora.Shared.Enums.DeliveryStatus.SENT"/>).
/// </para>
/// <para>
/// T37 ships stub implementations for <see cref="NotificationChannel.EMAIL"/>,
/// <see cref="NotificationChannel.TELEGRAM"/> and <see cref="NotificationChannel.DISCORD"/>
/// that log the rendered template and return success. T78 / T79 / T80 swap in
/// the real Resend / Telegram Bot / Discord webhook clients via DI (no
/// dispatcher-side change required).
/// </para>
/// </remarks>
public interface INotificationChannelHandler
{
    NotificationChannel Channel { get; }

    Task SendAsync(
        string targetExternalId,
        RenderedNotificationTemplate rendered,
        CancellationToken cancellationToken);
}
