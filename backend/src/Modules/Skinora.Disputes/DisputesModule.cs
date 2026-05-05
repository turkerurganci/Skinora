using Microsoft.Extensions.DependencyInjection;
using Skinora.Disputes.Application.AutoCheckers;
using Skinora.Disputes.Application.Disputes;

namespace Skinora.Disputes;

/// <summary>
/// DI wiring for the Disputes module (T58 — 02 §10, 03 §6, 07 §7.8–§7.10).
/// </summary>
/// <remarks>
/// Registers the orchestration entry point (<see cref="IDisputeService"/>)
/// plus the three type-specific auto-checkers. Auto-checkers depend on the
/// <c>AppDbContext</c> and on
/// <see cref="Skinora.Transactions.Application.Steam.ISteamInventoryReader"/>
/// (registered by the Transactions module — stub today, sidecar-backed
/// after T67).
/// </remarks>
public static class DisputesModule
{
    public static IServiceCollection AddDisputesModule(this IServiceCollection services)
    {
        services.AddScoped<IDisputeService, DisputeService>();

        services.AddScoped<IPaymentDisputeAutoChecker, PaymentDisputeAutoChecker>();
        services.AddScoped<IDeliveryDisputeAutoChecker, DeliveryDisputeAutoChecker>();
        services.AddScoped<IWrongItemDisputeAutoChecker, WrongItemDisputeAutoChecker>();

        return services;
    }
}
