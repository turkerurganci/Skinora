using Skinora.Notifications.Application.Notifications;

namespace Skinora.Notifications.Tests.TestSupport;

/// <summary>
/// Test double for <see cref="INotificationDispatcher"/> that records every
/// <see cref="NotificationRequest"/> a consumer emits instead of running the
/// real fan-out pipeline. Lets consumer tests assert recipient/type/parameters
/// directly without a template resolver or DB writes.
/// </summary>
public sealed class RecordingNotificationDispatcher : INotificationDispatcher
{
    public List<NotificationRequest> Requests { get; } = [];

    public Task DispatchAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.CompletedTask;
    }
}
