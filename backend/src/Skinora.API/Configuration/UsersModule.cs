using Skinora.Transactions.Application.Wallet;
using Skinora.Users.Application.Profiles;
using Skinora.Users.Application.Wallet;

namespace Skinora.API.Configuration;

/// <summary>
/// DI registration for the Skinora.Users module — profile read services
/// (T33 — 07 §5.1, §5.2, §5.5) and wallet address management
/// (T34 — 07 §5.3, §5.4). The <see cref="IActiveTransactionCounter"/>
/// implementation lives in <c>Skinora.Transactions</c> but is registered here
/// since <c>Skinora.Users</c> cannot reference <c>Skinora.Transactions</c>
/// (the dependency points the other way); the API composition root is the
/// only place with both references.
/// </summary>
public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);

        services.AddScoped<IUserProfileService, UserProfileService>();

        // T34 — wallet address management (07 §5.3, §5.4)
        services.AddSingleton<ITrc20AddressValidator, Trc20AddressValidator>();
        services.AddSingleton<IWalletSanctionsCheck, NoMatchWalletSanctionsCheck>();
        services.AddScoped<IActiveTransactionCounter, ActiveTransactionCounter>();
        services.AddScoped<IWalletAddressService, WalletAddressService>();

        return services;
    }
}
