using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// Unit coverage for <see cref="AccountSuspendedNotificationConsumer"/> and
/// <see cref="AccountUnsuspendedNotificationConsumer"/> (T105a — 02 §14.0/§16.2,
/// AC6). Verifies the consumers translate the suspension lifecycle events into
/// the correct single-recipient notifications (suspended forwards the admin
/// reason; unsuspended carries no params) and honour the consumer-idempotency
/// contract inherited from <see cref="NotificationConsumerBase{TEvent}"/>.
/// </summary>
public class AccountSuspensionNotificationConsumerTests
{
    [Fact]
    public async Task Suspended_Notifies_Affected_User_With_Reason()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new AccountSuspendedNotificationConsumer(
            dispatcher, processed,
            NullLogger<AccountSuspendedNotificationConsumer>.Instance);

        var userId = Guid.NewGuid();
        var domainEvent = new AccountSuspendedEvent(
            EventId: Guid.NewGuid(),
            UserId: userId,
            Reason: "Multi-account fraud detected",
            ExpiresAt: null,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(userId, request.UserId);
        Assert.Equal(NotificationType.ACCOUNT_SUSPENDED, request.Type);
        Assert.Equal("Multi-account fraud detected", request.Parameters["Reason"]);
    }

    [Fact]
    public async Task Suspended_Idempotent_When_EventAlreadyProcessed()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new AccountSuspendedNotificationConsumer(
            dispatcher, processed,
            NullLogger<AccountSuspendedNotificationConsumer>.Instance);

        var domainEvent = new AccountSuspendedEvent(
            EventId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            Reason: "Replay test reason",
            ExpiresAt: null,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Single(dispatcher.Requests);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Single(dispatcher.Requests);
    }

    [Fact]
    public async Task Unsuspended_Notifies_Affected_User()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new AccountUnsuspendedNotificationConsumer(
            dispatcher, processed,
            NullLogger<AccountUnsuspendedNotificationConsumer>.Instance);

        var userId = Guid.NewGuid();
        var domainEvent = new AccountUnsuspendedEvent(
            EventId: Guid.NewGuid(),
            UserId: userId,
            Automatic: true,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(userId, request.UserId);
        Assert.Equal(NotificationType.ACCOUNT_UNSUSPENDED, request.Type);
    }

    [Fact]
    public async Task Unsuspended_Idempotent_When_EventAlreadyProcessed()
    {
        var dispatcher = new RecordingDispatcher();
        var processed = new InMemoryProcessedEventStore();
        var sut = new AccountUnsuspendedNotificationConsumer(
            dispatcher, processed,
            NullLogger<AccountUnsuspendedNotificationConsumer>.Instance);

        var domainEvent = new AccountUnsuspendedEvent(
            EventId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            Automatic: false,
            OccurredAt: DateTime.UtcNow);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Single(dispatcher.Requests);

        await sut.Handle(domainEvent, CancellationToken.None);
        Assert.Single(dispatcher.Requests);
    }

    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        public List<NotificationRequest> Requests { get; } = [];

        public Task DispatchAsync(NotificationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryProcessedEventStore : IProcessedEventStore
    {
        private readonly HashSet<(Guid eventId, string consumer)> _entries = new();

        public Task<bool> ExistsAsync(
            Guid eventId, string consumerName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_entries.Contains((eventId, consumerName)));

        public Task MarkAsProcessedAsync(
            Guid eventId, string consumerName,
            CancellationToken cancellationToken = default)
        {
            _entries.Add((eventId, consumerName));
            return Task.CompletedTask;
        }
    }
}
