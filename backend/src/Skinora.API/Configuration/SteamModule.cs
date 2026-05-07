using Skinora.Steam.Application.Admin;

namespace Skinora.API.Configuration;

/// <summary>
/// DI registration for the Skinora.Steam module — currently exposes the
/// admin steam-accounts read service (T63, AD10). Sidecar adapters land
/// with T64–T69 and will register here too.
/// </summary>
public static class SteamModule
{
    public static IServiceCollection AddSteamModule(this IServiceCollection services)
    {
        services.AddScoped<IAdminSteamBotQueryService, AdminSteamBotQueryService>();
        return services;
    }
}
