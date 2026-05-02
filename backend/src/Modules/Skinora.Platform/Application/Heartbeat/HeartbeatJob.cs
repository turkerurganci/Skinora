using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Persistence;

namespace Skinora.Platform.Application.Heartbeat;

/// <summary>
/// Default <see cref="IHeartbeatJob"/> — self-rescheduling job (09 §13.4)
/// that stamps <c>UtcNow</c> onto the singleton <c>SystemHeartbeats</c> row.
/// </summary>
public sealed class HeartbeatJob : IHeartbeatJob
{
    private readonly AppDbContext _db;
    private readonly IBackgroundJobScheduler _scheduler;
    private readonly TimeProvider _clock;
    private readonly HeartbeatOptions _options;
    private readonly ILogger<HeartbeatJob> _logger;

    public HeartbeatJob(
        AppDbContext db,
        IBackgroundJobScheduler scheduler,
        TimeProvider clock,
        IOptions<HeartbeatOptions> options,
        ILogger<HeartbeatJob> logger)
    {
        _db = db;
        _scheduler = scheduler;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task TickAsync()
    {
        try
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var heartbeat = await _db.Set<SystemHeartbeat>()
                .FirstOrDefaultAsync(h => h.Id == SeedConstants.SystemHeartbeatId);
            if (heartbeat is null)
            {
                _logger.LogWarning(
                    "SystemHeartbeats singleton row missing — skipping tick. Seed should restore it on next migrate.");
                return;
            }

            heartbeat.LastHeartbeat = now;
            heartbeat.UpdatedAt = now;
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat tick failed.");
        }
        finally
        {
            try
            {
                _scheduler.Schedule<IHeartbeatJob>(
                    j => j.TickAsync(),
                    TimeSpan.FromSeconds(_options.IntervalSeconds));
            }
            catch (Exception scheduleEx)
            {
                _logger.LogCritical(
                    scheduleEx,
                    "Heartbeat could not reschedule itself — chain broken until restart.");
            }
        }
    }
}
