using Microsoft.Extensions.DependencyInjection.Extensions;
using Skinora.Fraud.Application.Account;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Pricing;
using Skinora.Transactions.Application.Steam;

namespace Skinora.API.Configuration;

/// <summary>
/// DI registration for the Skinora.Transactions module — T45 lifecycle
/// services (eligibility, params, creation), T67/T81 forward-deferred
/// stub ports (Steam inventory, market price), and the cross-module
/// glue that wires <c>IAccountFlagChecker</c> implemented in
/// <c>Skinora.Fraud</c> against the port declared inside
/// <c>Skinora.Transactions</c>.
/// </summary>
public static class TransactionsModule
{
    public static IServiceCollection AddTransactionsModule(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);

        // T45 — lifecycle services (07 §7.1–§7.4).
        services.AddScoped<ITransactionLimitsProvider, TransactionLimitsProvider>();
        services.AddScoped<ITransactionParamsService, TransactionParamsService>();
        services.AddScoped<ITransactionEligibilityService, TransactionEligibilityService>();
        services.AddScoped<IFraudPreCheckService, FraudPreCheckService>();
        services.AddScoped<ITransactionCreationService, TransactionCreationService>();
        services.AddSingleton<IInvitationCodeGenerator, InvitationCodeGenerator>();

        // T67 forward-deferred — Steam inventory stub. Tests inject their own
        // ISteamInventoryReader; production fails closed
        // (STEAM_INVENTORY_UNAVAILABLE) until T67 wires the real sidecar.
        services.TryAddScoped<ISteamInventoryReader, StubSteamInventoryReader>();

        // T81 forward-deferred — market price stub. Defaults to "no market
        // signal" so the fraud pre-check never fires until T81 ships.
        services.TryAddScoped<IMarketPriceProvider, NullMarketPriceProvider>();

        // Cross-module: IAccountFlagChecker is declared in Skinora.Transactions
        // but implemented in Skinora.Fraud (Fraud already references
        // Transactions; the reverse direction would be a project cycle).
        services.AddScoped<IAccountFlagChecker, AccountFlagChecker>();

        return services;
    }
}
