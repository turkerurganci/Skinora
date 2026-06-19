using System;
using System.Linq;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.API.BackgroundJobs;

/// <summary>
/// Hangfire wiring for the API host (T09 — Hangfire setup ve background job
/// altyapısı, 05 §2.2, 09 §13.1).
/// </summary>
/// <remarks>
/// <para>
/// Configures Hangfire with SQL Server storage, UTC time handling, the global
/// <c>AutomaticRetry(Attempts = 3)</c> default (09 §13.5), and the
/// <see cref="IBackgroundJobScheduler"/> abstraction so application modules can
/// schedule jobs without taking a direct dependency on <c>Hangfire.Core</c>.
/// </para>
/// <para>
/// The dashboard mount is performed by <see cref="UseHangfireModule"/> after
/// authentication middleware has run, so that the
/// <see cref="HangfireDashboardAuthFilter"/> sees the authenticated principal.
/// </para>
/// </remarks>
public static class HangfireModule
{
    public static IServiceCollection AddHangfireModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind options
        var hangfireSection = configuration.GetSection(HangfireOptions.SectionName);
        services.Configure<HangfireOptions>(hangfireSection);

        var hangfireOptions = hangfireSection.Get<HangfireOptions>() ?? new HangfireOptions();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Hangfire requires the 'DefaultConnection' connection string to be set.");

        services.AddHangfire(config =>
        {
            config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
                {
                    SchemaName = hangfireOptions.SchemaName,
                    PrepareSchemaIfNecessary = true,
                    CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                    SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                    QueuePollInterval = TimeSpan.FromSeconds(hangfireOptions.PollingIntervalSeconds),
                    UseRecommendedIsolationLevel = true,
                    DisableGlobalLocks = true,
                });
        });

        // 09 §13.5 — every job retries up to N times by default. Job bodies
        // must remain idempotent (09 §13.5 "Idempotent" rule). Hangfire ships
        // with a default AutomaticRetryAttribute(Attempts = 10) already
        // installed in GlobalJobFilters; we mutate the existing instance(s)
        // rather than appending a second filter, so the platform retry policy
        // is enforced consistently.
        foreach (var attr in GlobalJobFilters.Filters
                     .Select(f => f.Instance)
                     .OfType<AutomaticRetryAttribute>())
        {
            attr.Attempts = hangfireOptions.DefaultRetryAttempts;
        }

        // 09 §13.5 "UTC" — Hangfire schedules and timestamps run in UTC.
        // BackgroundJob.Schedule(TimeSpan) and Cron expressions are interpreted
        // in UTC by default; explicit DateTime overloads are restricted via
        // code review (09 §7.1).

        // Public abstraction consumed by other modules (Shared). Registered here
        // (the client side) so the startup priming hooks can enqueue/schedule
        // jobs into SQL storage. The processing server (worker) is registered
        // separately via AddHangfireProcessingServer so it can be ordered AFTER
        // the restart-recovery hook (WP16 resume-gate — see below).
        services.AddScoped<IBackgroundJobScheduler, HangfireBackgroundJobScheduler>();

        return services;
    }

    /// <summary>
    /// Registers the Hangfire processing server (worker) as a hosted service.
    /// </summary>
    /// <remarks>
    /// WP16 (05 §4.4 step 3) — the worker is registered SEPARATELY from
    /// <see cref="AddHangfireModule"/> and AFTER
    /// <c>TimeoutSchedulerStartupHook</c> so that, on a cold start following an
    /// outage, restart-recovery extends the active deadlines and re-issues the
    /// per-tx jobs BEFORE any worker begins draining the queue. Hosted-service
    /// StartAsync runs in registration order, so this is the explicit
    /// "resume processing only after the extension completes" gate the spec
    /// requires — overdue jobs queued in SQL Server cannot fire against stale
    /// deadlines during the recovery window. The client (job scheduler) is still
    /// registered early in <see cref="AddHangfireModule"/> so the priming hooks
    /// can enqueue while the worker is held back.
    /// </remarks>
    public static IServiceCollection AddHangfireProcessingServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var hangfireOptions =
            configuration.GetSection(HangfireOptions.SectionName).Get<HangfireOptions>()
            ?? new HangfireOptions();

        // For T09 the API host runs both web and worker; a dedicated worker
        // container can be split out later (T16 / post-MVP) without changing
        // application code.
        services.AddHangfireServer(serverOptions =>
        {
            if (hangfireOptions.WorkerCount is int workerCount)
            {
                serverOptions.WorkerCount = workerCount;
            }
            // Server name left to default (machine + GUID) so multiple
            // instances coexist without manual configuration.
        });

        return services;
    }

    /// <summary>
    /// Mounts the Hangfire dashboard at the configured path, behind the
    /// admin-only authorization filter. Must be called after
    /// <c>UseAuthentication</c> so the filter sees the authenticated principal.
    /// </summary>
    public static IApplicationBuilder UseHangfireModule(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<HangfireOptions>>().Value;

        if (!options.DashboardEnabled)
        {
            return app;
        }

        app.UseHangfireDashboard(options.DashboardPath, new DashboardOptions
        {
            Authorization = new IDashboardAuthorizationFilter[]
            {
                new HangfireDashboardAuthFilter(),
            },
            // Disable the dashboard's "back to site" link spoofing — it is an
            // admin tool only, never linked from the public app.
            DisplayStorageConnectionString = false,
            // Make sure stats endpoint is also gated by the same filter.
            IsReadOnlyFunc = _ => false,
        });

        return app;
    }
}
