using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IProcessedEventStore"/> for consumer tests — tracks
/// (eventId, consumerName) pairs so the
/// <see cref="Skinora.Notifications.Application.EventHandlers.NotificationConsumerBase{TEvent}"/>
/// idempotency guard can be exercised without a database.
/// </summary>
public sealed class InMemoryProcessedEventStore : IProcessedEventStore
{
    private readonly HashSet<(Guid EventId, string Consumer)> _entries = [];

    public Task<bool> ExistsAsync(
        Guid eventId,
        string consumerName,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_entries.Contains((eventId, consumerName)));

    public Task MarkAsProcessedAsync(
        Guid eventId,
        string consumerName,
        CancellationToken cancellationToken = default)
    {
        _entries.Add((eventId, consumerName));
        return Task.CompletedTask;
    }
}
