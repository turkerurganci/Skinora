using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skinora.Platform.Application.Settings;

namespace Skinora.Platform;

/// <summary>
/// DI wiring for the Skinora.Platform module — admin-managed system settings
/// service backing 07 §9.8–§9.9 (T41).
/// </summary>
public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ISystemSettingsService, SystemSettingsService>();
        return services;
    }
}
