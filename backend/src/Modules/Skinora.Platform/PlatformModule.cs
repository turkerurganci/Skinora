using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Application.Settings;

namespace Skinora.Platform;

/// <summary>
/// DI wiring for the Skinora.Platform module — system settings (T41) and the
/// central audit logger + query service (T42, 07 §9.19, 09 §18.6).
/// </summary>
public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ISystemSettingsService, SystemSettingsService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();
        return services;
    }
}
