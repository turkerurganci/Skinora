using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Skinora.Platform.Application.Heartbeat;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;

namespace Skinora.Platform.Tests.Integration;

/// <summary>
/// Integration coverage for <see cref="HeartbeatJob"/> (T47, 05 §4.4 heartbeat
/// pattern). Runs against a real <c>SystemHeartbeats</c> seed row supplied by
/// the <c>SystemHeartbeatConfiguration</c> migration.
/// </summary>
public class HeartbeatJobTests : IntegrationTestBase
{
    static HeartbeatJobTests()
    {
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private FakeTimeProvider _clock = null!;
    private CapturingScheduler _scheduler = null!;

    protected override Task SeedAsync(AppDbContext context)
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero));
        _scheduler = new CapturingScheduler();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Tick_Updates_LastHeartbeat_To_Utc_Now()
    {
        var sut = new HeartbeatJob(
            Context, _scheduler, _clock,
            Options.Create(new HeartbeatOptions { IntervalSeconds = 30 }),
            NullLogger<HeartbeatJob>.Instance);

        await sut.TickAsync();

        var persisted = await Context.Set<SystemHeartbeat>()
            .AsNoTracking()
            .SingleAsync(h => h.Id == SeedConstants.SystemHeartbeatId);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.LastHeartbeat);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.UpdatedAt);
    }

    [Fact]
    public async Task Tick_Reschedules_Itself_With_Configured_Interval()
    {
        var sut = new HeartbeatJob(
            Context, _scheduler, _clock,
            Options.Create(new HeartbeatOptions { IntervalSeconds = 45 }),
            NullLogger<HeartbeatJob>.Instance);

        await sut.TickAsync();

        var rescheduled = Assert.Single(_scheduler.ScheduledCalls);
        Assert.Equal(typeof(IHeartbeatJob), rescheduled.TargetType);
        Assert.Equal(TimeSpan.FromSeconds(45), rescheduled.Delay);
    }

    [Fact]
    public async Task Tick_Reschedules_Even_When_Save_Fails()
    {
        // Drop the heartbeat row before the tick — Save target missing → log + reschedule.
        var existing = await Context.Set<SystemHeartbeat>().SingleAsync();
        Context.Set<SystemHeartbeat>().Remove(existing);
        await Context.SaveChangesAsync();

        var sut = new HeartbeatJob(
            Context, _scheduler, _clock,
            Options.Create(new HeartbeatOptions { IntervalSeconds = 60 }),
            NullLogger<HeartbeatJob>.Instance);

        await sut.TickAsync();

        Assert.Single(_scheduler.ScheduledCalls);
    }

    private sealed class CapturingScheduler : IBackgroundJobScheduler
    {
        public List<ScheduledCall> ScheduledCalls { get; } = new();

        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
        {
            ScheduledCalls.Add(new ScheduledCall(typeof(T), delay));
            return Guid.NewGuid().ToString("N");
        }
        public string Enqueue<T>(Expression<Action<T>> methodCall) => Guid.NewGuid().ToString("N");
        public bool Delete(string jobId) => true;
        public void AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>> methodCall, string cronExpression) { }

        public sealed record ScheduledCall(Type TargetType, TimeSpan Delay);
    }
}
