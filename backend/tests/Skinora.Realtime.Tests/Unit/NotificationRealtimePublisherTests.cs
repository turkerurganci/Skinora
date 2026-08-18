using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Realtime.Application.Contracts;
using Skinora.Realtime.Hubs;
using Skinora.Realtime.Infrastructure;

namespace Skinora.Realtime.Tests.Unit;

/// <summary>
/// WP9 — proves <see cref="SignalRNotificationRealtimePublisher"/> routes the
/// three admin-scoped events to the admin-only group (T69 K4 — no
/// <c>Clients.All</c> leak of bot / wallet / reconciliation data), user-scoped
/// events to the per-user group, and the maintenance banner to all clients.
/// </summary>
public class NotificationRealtimePublisherTests
{
    private static (SignalRNotificationRealtimePublisher Sut, RecordingHubClients Clients) CreateSut()
    {
        var clients = new RecordingHubClients();
        var sut = new SignalRNotificationRealtimePublisher(
            new RecordingHubContext(clients),
            NullLogger<SignalRNotificationRealtimePublisher>.Instance);
        return (sut, clients);
    }

    [Fact]
    public async Task AdminReconciliationMismatch_TargetsAdminGroup()
    {
        var (sut, clients) = CreateSut();

        await sut.PublishAdminReconciliationMismatchAsync(
            new NotificationRealtimePayloads.AdminReconciliationMismatch(
                "HotWallet", "TRC20ADDR", "USDT", 100m, 90m, -10m, 123L, DateTime.UtcNow),
            CancellationToken.None);

        var send = Assert.Single(clients.Sends);
        Assert.Equal(NotificationsHub.AdminGroup, send.Target);
        Assert.Equal("AdminReconciliationMismatch", send.Method);
    }

    [Fact]
    public async Task AdminHotWalletThresholdBreached_TargetsAdminGroup()
    {
        var (sut, clients) = CreateSut();

        await sut.PublishAdminHotWalletThresholdBreachedAsync(
            new NotificationRealtimePayloads.AdminHotWalletThresholdBreached(
                "USDT", "Upper", 5000m, 6000m, 456L, DateTime.UtcNow),
            CancellationToken.None);

        var send = Assert.Single(clients.Sends);
        Assert.Equal(NotificationsHub.AdminGroup, send.Target);
        Assert.Equal("AdminHotWalletThresholdBreached", send.Method);
    }

    [Fact]
    public async Task NewNotification_TargetsPerUserGroup()
    {
        var (sut, clients) = CreateSut();
        var userId = Guid.NewGuid();

        await sut.PublishNewNotificationAsync(
            userId,
            new NotificationRealtimePayloads.NewNotification(
                Guid.NewGuid(), "BUYER_ACCEPTED", "msg", null, null, DateTime.UtcNow),
            CancellationToken.None);

        var send = Assert.Single(clients.Sends);
        Assert.Equal(NotificationsHub.GroupName(userId), send.Target);
        Assert.Equal("NewNotification", send.Method);
    }

    [Fact]
    public async Task MaintenanceStatusChanged_BroadcastsToAll()
    {
        var (sut, clients) = CreateSut();

        await sut.PublishMaintenanceStatusChangedAsync(
            new NotificationRealtimePayloads.MaintenanceStatusChanged(
                true, "STEAM_OUTAGE", "Bakım", null),
            CancellationToken.None);

        var send = Assert.Single(clients.Sends);
        Assert.Equal(RecordingHubClients.AllTarget, send.Target);
        Assert.Equal("MaintenanceStatusChanged", send.Method);
    }

    // ---------- hand-rolled IHubContext fake (project convention: no Moq) ----------

    private sealed record RecordedSend(string Target, string Method, object?[] Args);

    private sealed class RecordingHubContext(IHubClients clients) : IHubContext<NotificationsHub>
    {
        public IHubClients Clients { get; } = clients;
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class RecordingHubClients : IHubClients
    {
        public const string AllTarget = "<all>";

        public List<RecordedSend> Sends { get; } = [];

        public IClientProxy All => new RecordingProxy(AllTarget, Sends);
        public IClientProxy Group(string groupName) => new RecordingProxy(groupName, Sends);

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
            throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) =>
            throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) =>
            throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
            throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class RecordingProxy(string target, List<RecordedSend> sends) : IClientProxy
    {
        public Task SendCoreAsync(
            string method, object?[] args, CancellationToken cancellationToken = default)
        {
            sends.Add(new RecordedSend(target, method, args));
            return Task.CompletedTask;
        }
    }
}
