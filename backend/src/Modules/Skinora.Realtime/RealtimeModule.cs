using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Countdown;
using Skinora.Realtime.Infrastructure;

namespace Skinora.Realtime;

/// <summary>
/// DI wiring for the Realtime module (T61 — 07 §11.1 RT1 transaction hub).
/// </summary>
/// <remarks>
/// Registers:
/// <list type="bullet">
///   <item><see cref="ITransactionRealtimePublisher"/> backed by SignalR
///         (<see cref="SignalRTransactionRealtimePublisher"/>).</item>
///   <item><see cref="CountdownSyncBroadcaster"/> as a hosted service running
///         the 30-second sweep mandated by 07 §11.1.</item>
/// </list>
/// MediatR consumer registrations (the eight RT1 event handlers) are picked up
/// by the outbox module's assembly scan when this module's assembly is added
/// to the scan list.
/// </remarks>
public static class RealtimeModule
{
    public static IServiceCollection AddRealtimeModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CountdownSyncOptions>(
            configuration.GetSection(CountdownSyncOptions.SectionName));

        services.AddScoped<ITransactionRealtimePublisher, SignalRTransactionRealtimePublisher>();

        services.AddHostedService<CountdownSyncBroadcaster>();

        return services;
    }
}
