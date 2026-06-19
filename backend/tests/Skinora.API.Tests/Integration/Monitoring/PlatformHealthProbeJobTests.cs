using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Skinora.API.Monitoring;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Domain;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.API.Tests.Integration.Monitoring;

/// <summary>
/// WP16 — integration coverage for <see cref="PlatformHealthProbeJob"/>: the
/// outage / recovery transitions must write a <c>PLATFORM_OUTAGE_DETECTED</c>
/// audit row (SECURITY_EVENT, SYSTEM actor) and publish a
/// <see cref="PlatformOutageAlertEvent"/> to the outbox so admins are alerted
/// (05 §4.4, 02 §3.3). Alert-only — no freeze is applied.
/// </summary>
public class PlatformHealthProbeJobTests : IntegrationTestBase
{
    static PlatformHealthProbeJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const int Threshold = 2;

    private readonly FakeSidecarHealthClient _health = new();
    private readonly RecordingOutbox _outbox = new();
    private readonly PlatformHealthMonitorState _state = new();
    private readonly FakeTimeProvider _clock =
        new(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));

    private PlatformHealthProbeJob BuildSut() => new(
        _health,
        _state,
        _outbox,
        new AuditLogger(Context, _clock),
        Context,
        _clock,
        Options.Create(new HealthProbeOptions { FailureThreshold = Threshold }),
        NullLogger<PlatformHealthProbeJob>.Instance);

    [Fact]
    public async Task Sustained_Outage_Then_Recovery_Alerts_Once_Each()
    {
        var sut = BuildSut();

        // Probe 1 — Steam down, below threshold → no alert.
        _health.SteamHealthy = false;
        await sut.ProbeAsync();
        Assert.Empty(_outbox.Events.OfType<PlatformOutageAlertEvent>());
        Assert.False(await Context.Set<AuditLog>()
            .AnyAsync(a => a.Action == AuditAction.PLATFORM_OUTAGE_DETECTED));

        // Probe 2 — Steam still down, threshold crossed → DEGRADED alert.
        await sut.ProbeAsync();
        var degraded = Assert.Single(_outbox.Events.OfType<PlatformOutageAlertEvent>());
        Assert.Equal(PlatformComponents.Steam, degraded.Component);
        Assert.Equal("DEGRADED", degraded.Status);

        var degradedAudit = await Context.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == AuditAction.PLATFORM_OUTAGE_DETECTED);
        Assert.Equal(ActorType.SYSTEM, degradedAudit.ActorType);
        Assert.Equal(SeedConstants.SystemUserId, degradedAudit.ActorId);
        Assert.Equal("PlatformHealth", degradedAudit.EntityType);
        Assert.Equal(PlatformComponents.Steam, degradedAudit.EntityId);
        Assert.Contains("\"status\":\"DEGRADED\"", degradedAudit.NewValue);

        // Probe 3 — Steam healthy again → RECOVERED alert.
        _health.SteamHealthy = true;
        await sut.ProbeAsync();
        Assert.Equal(2, _outbox.Events.OfType<PlatformOutageAlertEvent>().Count());
        var recovered = _outbox.Events.OfType<PlatformOutageAlertEvent>().Last();
        Assert.Equal("RECOVERED", recovered.Status);
        Assert.Equal(2, await Context.Set<AuditLog>()
            .CountAsync(a => a.Action == AuditAction.PLATFORM_OUTAGE_DETECTED));
    }

    [Fact]
    public async Task Unconfigured_Component_Is_Skipped()
    {
        var sut = BuildSut();

        // Both components report "not configured" (null) → no probe, no alert.
        _health.SteamHealthy = null;
        _health.BlockchainHealthy = null;
        await sut.ProbeAsync();
        await sut.ProbeAsync();
        await sut.ProbeAsync();

        Assert.Empty(_outbox.Events.OfType<PlatformOutageAlertEvent>());
        Assert.False(await Context.Set<AuditLog>()
            .AnyAsync(a => a.Action == AuditAction.PLATFORM_OUTAGE_DETECTED));
    }

    private sealed class FakeSidecarHealthClient : ISidecarHealthClient
    {
        public bool? SteamHealthy { get; set; } = true;
        public bool? BlockchainHealthy { get; set; } = null;

        public Task<bool?> IsHealthyAsync(string component, CancellationToken cancellationToken)
            => Task.FromResult(component == PlatformComponents.Steam ? SteamHealthy : BlockchainHealthy);
    }

    private sealed class RecordingOutbox : IOutboxService
    {
        public List<IDomainEvent> Events { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
