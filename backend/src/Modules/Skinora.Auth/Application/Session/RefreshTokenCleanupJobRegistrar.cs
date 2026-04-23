using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.Auth.Application.Session;

/// <summary>
/// Registers <see cref="RefreshTokenCleanupJob"/> as a daily recurring
/// Hangfire job at startup (03:00 UTC). Using an <see cref="IHostedService"/>
/// keeps the job definition in code and rewrites the registration every boot
/// so a change to the cron expression propagates on the next deploy.
/// </summary>
/// <remarks>
/// <see cref="IBackgroundJobScheduler"/> is scoped (Hangfire's
/// <c>IBackgroundJobClient</c> is a scoped service), so we take an
/// <see cref="IServiceScopeFactory"/> and open a scope per start — the
/// same pattern used by <c>OutboxStartupHook</c>. Using the scoped service
/// directly trips ASP.NET Core's DI validator (singleton → scoped) and
/// crashes host start-up in every test that builds the app.
/// </remarks>
public sealed class RefreshTokenCleanupJobRegistrar : IHostedService
{
    private const string DailyCron = "0 3 * * *";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefreshTokenCleanupJobRegistrar> _logger;

    public RefreshTokenCleanupJobRegistrar(
        IServiceScopeFactory scopeFactory,
        ILogger<RefreshTokenCleanupJobRegistrar> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundJobScheduler>();

            scheduler.AddOrUpdateRecurring<RefreshTokenCleanupJob>(
                RefreshTokenCleanupJob.RecurringJobId,
                job => job.Execute(),
                DailyCron);
        }
        catch (Exception ex)
        {
            // Don't block startup — an unavailable scheduler in non-production
            // hosts (e.g. migrations-only tooling) should not prevent the app
            // from coming up. The cleanup job is a maintenance concern, not a
            // correctness one: expired tokens still fail rotation in the
            // service layer even without periodic sweeping.
            _logger.LogWarning(ex,
                "RefreshTokenCleanupJobRegistrar failed to register the recurring job.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
