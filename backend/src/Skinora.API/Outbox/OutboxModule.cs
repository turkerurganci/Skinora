using System.Reflection;
using Medallion.Threading;
using Medallion.Threading.SqlServer;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Outbox;

namespace Skinora.API.Outbox;

/// <summary>
/// DI wiring for the outbox pattern (T10 — outbox altyapısı, 05 §5.1,
/// 09 §9.3, §13.4).
/// </summary>
/// <remarks>
/// <para>
/// Registers the producer side (<see cref="IOutboxService"/>), the consumer
/// idempotency store, the external receiver-side idempotency service, the
/// dispatcher itself and its dependencies (MediatR publisher, Medallion
/// distributed lock provider over SQL Server, default admin alert sink).
/// </para>
/// <para>
/// MediatR is registered with the assemblies that contain
/// <c>INotificationHandler&lt;T&gt;</c> implementations. T10 only registers
/// the API host assembly; module assemblies that introduce handlers (T44+)
/// add themselves to the assembly list when they wire their own modules.
/// </para>
/// </remarks>
public static class OutboxModule
{
    public static IServiceCollection AddOutboxModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));

        // Producer / consumer / receiver — all scoped because they share the
        // request-scoped AppDbContext.
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<IProcessedEventStore, ProcessedEventStore>();
        services.AddScoped<IExternalIdempotencyService, ExternalIdempotencyService>();

        // Dispatcher — scoped because Hangfire creates a new scope per
        // invocation and the dispatcher pulls request-scoped dependencies
        // (DbContext, MediatR publisher).
        services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();

        // Admin alert hook — default logging sink. Replaceable by registering
        // another implementation later in the pipeline (notification module).
        services.AddScoped<IOutboxAdminAlertSink, LoggingOutboxAdminAlertSink>();

        // MediatR — used by the dispatcher to fan out events to in-process
        // INotificationHandler<TEvent> consumers (09 §9.3 example).
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(GetMediatRScanAssemblies());
        });

        // Distributed lock — Medallion over SQL Server using the same
        // connection string as EF Core / Hangfire (09 §13.4 tekillik
        // garantisi). The lock is held for the duration of the dispatcher
        // batch and released as the using-handle disposes.
        services.AddSingleton<IDistributedLockProvider>(_ =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Outbox dispatcher requires the 'DefaultConnection' connection string for the distributed lock provider.");

            return new SqlDistributedSynchronizationProvider(connectionString);
        });

        // The IHostedService that primes the dispatcher chain on startup.
        services.AddHostedService<OutboxStartupHook>();

        return services;
    }

    /// <summary>
    /// Returns the assemblies MediatR should scan for handler registrations.
    /// The API host plus every module assembly that ships at least one
    /// production <c>INotificationHandler&lt;T&gt;</c> consumer (T48 onwards).
    /// </summary>
    private static Assembly[] GetMediatRScanAssemblies() =>
    [
        typeof(OutboxModule).Assembly,
        typeof(Skinora.Notifications.NotificationsModule).Assembly,
    ];
}
