using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Tests.TestSupport;

/// <summary>
/// Spy implementation of <see cref="INotificationChannelHandler"/> that
/// records every send and optionally throws to simulate transient failures.
/// </summary>
public sealed class SpyNotificationChannelHandler : INotificationChannelHandler
{
    public SpyNotificationChannelHandler(NotificationChannel channel)
    {
        Channel = channel;
    }

    public NotificationChannel Channel { get; }

    public List<(string Target, RenderedNotificationTemplate Rendered)> Sends { get; } = new();

    public Func<Exception?>? ExceptionFactory { get; set; }

    public Task SendAsync(
        string targetExternalId,
        RenderedNotificationTemplate rendered,
        CancellationToken cancellationToken)
    {
        Sends.Add((targetExternalId, rendered));

        var ex = ExceptionFactory?.Invoke();
        if (ex is not null)
            throw ex;

        return Task.CompletedTask;
    }
}
