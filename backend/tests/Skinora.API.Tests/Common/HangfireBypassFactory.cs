using System;
using System.Linq;
using Hangfire;
using Medallion.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skinora.API.BackgroundJobs;
using Skinora.API.Outbox;
using Skinora.API.Startup;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.API.Tests.Common;

/// <summary>
/// WebApplicationFactory base used by every integration test class in this
/// project. Replaces Hangfire's production SQL Server storage with
/// <c>InMemoryStorage</c> so the host can start without an actual SQL Server
/// instance reachable from the test runner.
/// </summary>
/// <remarks>
/// <para>
/// Without this swap, <c>HangfireModule.AddHangfireModule</c> registers
/// <c>SqlServerStorage</c> with <c>PrepareSchemaIfNecessary = true</c>; the
/// storage's <c>Initialize()</c> path attempts a real SQL connection during
/// host startup, retries four times against a 30-second TCP timeout, and
/// drags every test factory build out by ~30 seconds — which in turn pushes
/// xUnit's collection execution past its limits and causes unrelated
/// downstream tests to fail.
/// </para>
/// <para>
/// The Hangfire processing server is also removed. In Hangfire 1.8.x,
/// <c>AddHangfireServer</c> registers <c>IHostedService</c> via a factory
/// delegate (not a concrete type), so the namespace-based scrub catches it
/// only when the factory's declaring-type assembly is also checked. Without
/// removal, the server's non-graceful shutdown sets process exit code 1 even
/// though all tests pass — breaking CI. Tests that need to inspect
/// <see cref="GlobalJobFilters"/> can do so against the test host; the
/// production HangfireModule mutates the global filters in place, so
/// per-test re-application is unnecessary.
/// </para>
/// </remarks>
public class HangfireBypassFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Provide a well-formed (but never actually contacted) connection
        // string so HangfireModule's null-check passes during host build.
        builder.UseSetting(
            "ConnectionStrings:DefaultConnection",
            "Server=(local);Database=SkinoraTest;Integrated Security=true;TrustServerCertificate=true");

        builder.ConfigureServices(services =>
        {
            // Strip every Hangfire DI registration carried in by
            // HangfireModule.AddHangfireModule (storage, client, server,
            // hosted service). Service types live across the Hangfire.* and
            // Hangfire.AspNetCore.* assemblies, so a namespace-based scrub is
            // the most reliable approach. The third condition catches
            // factory-based registrations (e.g. AddHangfireServer in 1.8.x
            // registers IHostedService via delegate, leaving ImplementationType
            // null). Without this, the processing server survives, polls an
            // empty InMemoryStorage queue, and causes non-graceful shutdown
            // exit code 1 when the test host disposes.
            var hangfireDescriptors = services
                .Where(d =>
                    (d.ServiceType.Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) ?? false) ||
                    (d.ImplementationType?.Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) ?? false) ||
                    (d.ImplementationFactory?.Method.DeclaringType?.Assembly.GetName().Name?
                        .StartsWith("Hangfire", StringComparison.Ordinal) ?? false))
                .ToList();

            foreach (var d in hangfireDescriptors)
            {
                services.Remove(d);
            }

            // Re-register a minimal Hangfire pipeline backed by InMemoryStorage.
            services.AddHangfire(config =>
            {
                config
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UseInMemoryStorage();
            });

            // Mirror HangfireModule's in-place mutation of the global
            // AutomaticRetry filter (Attempts = 3 per 09 §13.5).
            foreach (var attr in GlobalJobFilters.Filters
                         .Select(f => f.Instance)
                         .OfType<AutomaticRetryAttribute>())
            {
                attr.Attempts = 3;
            }

            // The IBackgroundJobScheduler descriptor was scrubbed because its
            // implementation type lives in Skinora.API.BackgroundJobs (not
            // under Hangfire.*), but it depends on IBackgroundJobClient which
            // AddHangfire above re-registered. Re-add explicitly.
            services.AddScoped<IBackgroundJobScheduler, HangfireBackgroundJobScheduler>();

            // T10 — drop the OutboxStartupHook so the dispatcher chain never
            // primes in unrelated tests. The Hangfire chain would otherwise
            // try to resolve OutboxDispatcher and reach for AppDbContext /
            // Medallion's SqlDistributedSynchronizationProvider, both of
            // which require a real SQL Server.
            //
            // T26 — drop SettingsBootstrapHook for the same reason: its
            // StartAsync queries AppDbContext for SystemSettings, which in
            // these tests is rebuilt as an in-memory SQLite with no seed
            // rows. Two EF providers in DI (the original SqlServer plus the
            // SQLite replacement) also trip EF's single-provider guard.
            var startupHookDescriptors = services
                .Where(d =>
                    d.ImplementationType == typeof(OutboxStartupHook) ||
                    d.ImplementationType == typeof(SettingsBootstrapHook))
                .ToList();
            foreach (var d in startupHookDescriptors)
            {
                services.Remove(d);
            }

            // Replace Medallion's SQL Server lock provider with the in-process
            // semaphore stub so OutboxDispatcher unit tests can exercise the
            // tekillik garantisi without a database.
            var lockDescriptors = services
                .Where(d => d.ServiceType == typeof(IDistributedLockProvider))
                .ToList();
            foreach (var d in lockDescriptors)
            {
                services.Remove(d);
            }
            services.AddSingleton<IDistributedLockProvider, InMemoryDistributedLockProvider>();

            // T16 — clear production health checks (SQL Server, Redis) that
            // would fail without real infrastructure. Tests run without DB/Redis.
            var healthCheckDescriptors = services
                .Where(d =>
                    d.ServiceType.FullName?.Contains("HealthCheck", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var d in healthCheckDescriptors)
            {
                services.Remove(d);
            }
            services.AddHealthChecks();
        });
    }
}
