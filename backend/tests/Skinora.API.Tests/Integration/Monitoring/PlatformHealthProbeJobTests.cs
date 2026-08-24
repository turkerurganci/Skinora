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
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.API.Tests.Integration.Monitoring;

/// <summary>
/// WP16 — integration coverage for <see cref="PlatformHealthProbeJob"/>: the
/// outage / recovery transitions must write a <c>PLATFORM_OUTAGE_DETECTED</c>
/// audit row (SECURITY_EVENT, SYSTEM actor) and publish a
/// <see cref="PlatformOutageAlertEvent"/> to the outbox so admins are alerted
/// (05 §4.4, 02 §3.3).
/// </summary>
/// <remarks>
/// WP1 (T50) added the automatic bulk timeout freeze/resume on those same
/// edges. The freeze <i>engine</i> is covered by
/// <c>TimeoutFreezeServiceTests</c>; what these tests own is the probe's part
/// of the contract — the component → reason mapping, the edge → direction
/// mapping, and the fact that the bulk call's own <c>SaveChanges</c> cannot
/// split or duplicate the alert unit of work.
/// </remarks>
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
    private readonly RecordingFreezeService _freeze = new();

    private PlatformHealthProbeJob BuildSut() => new(
        _health,
        _state,
        _outbox,
        new AuditLogger(Context, _clock),
        _freeze,
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

    [Fact]
    public async Task Failed_Persist_Reverts_State_So_Next_Probe_Redetects()
    {
        // WP16 adversarial-review S2: a transient SaveChanges failure on the
        // degradation edge must NOT swallow the alert. Models production faithfully
        // — the monitor state is a singleton, but each Hangfire run gets its own
        // scoped AppDbContext (so the failed run's change tracker is discarded with
        // its disposed context; only the state survives to drive re-detection).
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        var probeOptions = Options.Create(new HealthProbeOptions { FailureThreshold = 1 });
        _health.SteamHealthy = false;

        // Run 1 — Degraded edge, but SaveChanges throws → caught + state reverted.
        await using (var throwingContext = new ThrowingOnceDbContext(options))
        {
            var failingSut = new PlatformHealthProbeJob(
                _health, _state, _outbox, new AuditLogger(throwingContext, _clock),
                _freeze, throwingContext, _clock, probeOptions,
                NullLogger<PlatformHealthProbeJob>.Instance);
            await failingSut.ProbeAsync();
        }
        Assert.False(await Context.Set<AuditLog>().AsNoTracking()
            .AnyAsync(a => a.Action == AuditAction.PLATFORM_OUTAGE_DETECTED));

        // Run 2 — fresh scoped context; the reverted state re-detects the outage
        // and the alert is finally persisted exactly once.
        await using (var freshContext = CreateContext())
        {
            var healthySut = new PlatformHealthProbeJob(
                _health, _state, _outbox, new AuditLogger(freshContext, _clock),
                _freeze, freshContext, _clock, probeOptions,
                NullLogger<PlatformHealthProbeJob>.Instance);
            await healthySut.ProbeAsync();
        }
        Assert.Equal(1, await Context.Set<AuditLog>().AsNoTracking()
            .CountAsync(a => a.Action == AuditAction.PLATFORM_OUTAGE_DETECTED));
    }

    // -------------------- WP1 (T50) automatic freeze / resume --------------------

    [Fact]
    public async Task Steam_Outage_Edge_Freezes_And_Recovery_Resumes_With_Steam_Reason()
    {
        var sut = BuildSut();
        _health.SteamHealthy = false;

        // Below the consecutive-failure threshold — no edge, so no freeze. This
        // is the debounce that makes the automatic freeze safe against a single
        // flaky probe.
        await sut.ProbeAsync();
        Assert.Empty(_freeze.Calls);

        // Threshold crossed → freeze the Steam-bound timeouts.
        await sut.ProbeAsync();
        Assert.Equal(("Freeze", TimeoutFreezeReason.STEAM_OUTAGE), Assert.Single(_freeze.Calls));

        // Still down → no new edge, no repeated freeze.
        await sut.ProbeAsync();
        Assert.Single(_freeze.Calls);

        // Recovery edge → resume with the same reason, so only the rows this
        // outage froze are released.
        _health.SteamHealthy = true;
        await sut.ProbeAsync();
        Assert.Equal(2, _freeze.Calls.Count);
        Assert.Equal(("Resume", TimeoutFreezeReason.STEAM_OUTAGE), _freeze.Calls[1]);
    }

    [Fact]
    public async Task Blockchain_Outage_Edge_Uses_Blockchain_Degradation_Reason()
    {
        var sut = BuildSut();
        _health.SteamHealthy = true;
        _health.BlockchainHealthy = false;

        await sut.ProbeAsync();
        await sut.ProbeAsync();

        // The two components must not share a reason: STEAM_OUTAGE and
        // BLOCKCHAIN_DEGRADATION freeze different transaction states
        // (TimeoutFreezeReasonScopes), and resume matches on the reason.
        Assert.Equal(
            ("Freeze", TimeoutFreezeReason.BLOCKCHAIN_DEGRADATION),
            Assert.Single(_freeze.Calls));
    }

    [Fact]
    public async Task Freeze_Failure_Still_Alerts_And_Records_Null_Affected_Count()
    {
        // The alert is the safety net: if the bulk freeze fails, admins must
        // still learn about the outage so they can apply the manual WP7 freeze.
        _freeze.ThrowOnNextCall = true;
        var sut = BuildSut();
        _health.SteamHealthy = false;

        await sut.ProbeAsync();
        await sut.ProbeAsync();

        // The freeze must actually have been attempted — without this the test
        // would pass just as well against a probe that never calls the freeze
        // service at all (measured by mutation, 2026-08-24).
        Assert.Equal(1, _freeze.Attempts);

        var alert = Assert.Single(_outbox.Events.OfType<PlatformOutageAlertEvent>());
        Assert.Equal("DEGRADED", alert.Status);

        var audit = await Context.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == AuditAction.PLATFORM_OUTAGE_DETECTED);
        Assert.Contains("\"timeoutsAffected\":null", audit.NewValue);
    }

    [Fact]
    public async Task Freeze_Inner_SaveChanges_Does_Not_Duplicate_Or_Split_The_Alert()
    {
        // FreezeManyAsync owns its own SaveChanges on the same scoped
        // AppDbContext. If the probe staged the audit + outbox rows first, that
        // inner commit would flush them early and the revert-on-failure path
        // would no longer govern what it wrote. Running the freeze first must
        // leave exactly one audit row and one alert.
        _freeze.SaveChangesOnCall = () => Context.SaveChangesAsync();
        var sut = BuildSut();
        _health.SteamHealthy = false;

        await sut.ProbeAsync();
        await sut.ProbeAsync();

        // Pin that the interleaving actually happened — otherwise a probe that
        // never calls the freeze service would satisfy the assertions below
        // trivially (measured by mutation, 2026-08-24).
        Assert.Equal(("Freeze", TimeoutFreezeReason.STEAM_OUTAGE), Assert.Single(_freeze.Calls));

        Assert.Single(_outbox.Events.OfType<PlatformOutageAlertEvent>());
        Assert.Equal(1, await Context.Set<AuditLog>().AsNoTracking()
            .CountAsync(a => a.Action == AuditAction.PLATFORM_OUTAGE_DETECTED));
    }

    private sealed class RecordingFreezeService : ITimeoutFreezeService
    {
        public List<(string Direction, TimeoutFreezeReason Reason)> Calls { get; } = [];

        /// <summary>
        /// Every bulk call reaching the service, including ones that throw —
        /// <see cref="Calls"/> only records the ones that got far enough to
        /// succeed.
        /// </summary>
        public int Attempts { get; private set; }

        /// <summary>Set to make the next bulk call throw (freeze-failure path).</summary>
        public bool ThrowOnNextCall { get; set; }

        /// <summary>
        /// Set to reproduce the real service's own <c>SaveChangesAsync</c> so
        /// the alert unit of work is exercised against a mid-probe commit.
        /// </summary>
        public Func<Task>? SaveChangesOnCall { get; set; }

        public Task<int> FreezeManyAsync(TimeoutFreezeReason reason, CancellationToken cancellationToken)
            => RecordAsync("Freeze", reason);

        public Task<int> ResumeManyAsync(TimeoutFreezeReason reason, CancellationToken cancellationToken)
            => RecordAsync("Resume", reason);

        private async Task<int> RecordAsync(string direction, TimeoutFreezeReason reason)
        {
            Attempts++;

            if (ThrowOnNextCall)
            {
                ThrowOnNextCall = false;
                throw new InvalidOperationException("Simulated bulk freeze failure.");
            }

            Calls.Add((direction, reason));

            if (SaveChangesOnCall is not null)
                await SaveChangesOnCall();

            return 0;
        }

        // Single-transaction overloads are the T59 emergency-hold path — the
        // probe never touches them.
        public Task FreezeAsync(Transaction transaction, TimeoutFreezeReason reason, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ResumeAsync(Transaction transaction, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingOnceDbContext : AppDbContext
    {
        private bool _thrown;

        public ThrowingOnceDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_thrown)
            {
                _thrown = true;
                throw new DbUpdateException("Simulated transient persistence failure.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
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
