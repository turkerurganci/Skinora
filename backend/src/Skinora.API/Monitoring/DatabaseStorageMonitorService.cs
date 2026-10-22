using Microsoft.Extensions.Options;

namespace Skinora.API.Monitoring;

/// <summary>
/// T164 — reads the database size and backup history every
/// <see cref="DatabaseStorageMonitorOptions.IntervalSeconds"/> and publishes it
/// (<see cref="PrometheusDatabaseStorageObserver"/>). A hosted loop, not a
/// Hangfire job: the reading must not depend on the job storage that lives in
/// the very database it watches, and it has no state to make idempotent. A
/// failed reading is logged and retried on the next tick; it never stops the
/// host.
/// </summary>
public sealed class DatabaseStorageMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PrometheusDatabaseStorageObserver _observer;
    private readonly DatabaseStorageMonitorOptions _options;
    private readonly ILogger<DatabaseStorageMonitorService> _logger;

    public DatabaseStorageMonitorService(
        IServiceScopeFactory scopeFactory,
        PrometheusDatabaseStorageObserver observer,
        IOptions<DatabaseStorageMonitorOptions> options,
        ILogger<DatabaseStorageMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _observer = observer;
        _options = options.Value;
        _logger = logger;
    }

    public TimeSpan Interval => TimeSpan.FromSeconds(
        Math.Max(_options.IntervalSeconds, DatabaseStorageMonitorOptions.MinimumIntervalSeconds));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Database storage monitor disabled (DatabaseStorageMonitor:Enabled=false) — no size or backup series.");
            return;
        }

        await Task.Yield();
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await ProbeOnceAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    /// <summary>One reading; <see langword="true"/> when something was published.</summary>
    public async Task<bool> ProbeOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var probe = scope.ServiceProvider.GetRequiredService<IDatabaseStorageProbe>();
            var snapshot = await probe.ReadAsync(cancellationToken);
            if (snapshot is null) return false;
            _observer.Report(snapshot);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _observer.ReportFailure();
            _logger.LogWarning(ex,
                "Database storage reading failed — size and backup series keep their last value, skinora_backend_db_probe_success is 0 (db-monitor-probe-failing after 15 min); retrying in {Interval}.",
                Interval);
            return false;
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
