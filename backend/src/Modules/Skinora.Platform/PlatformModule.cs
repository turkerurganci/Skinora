using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Application.Heartbeat;
using Skinora.Platform.Application.Settings;

namespace Skinora.Platform;

/// <summary>
/// DI wiring for the Skinora.Platform module — system settings (T41), the
/// central audit logger + query service (T42, 07 §9.19, 09 §18.6) and the
/// platform heartbeat (T47). <see cref="HeartbeatOptions"/> binding lives in
/// the API host (Program.cs) so this assembly does not need the
/// Microsoft.Extensions.Configuration.Binder dependency that
/// <c>Microsoft.NET.Sdk.Web</c> ships out of the box.
/// </summary>
public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ISystemSettingsService, SystemSettingsService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();

        // T47 — platform heartbeat (self-rescheduling job target). Caller
        // (Program.cs) binds HeartbeatOptions before this is invoked.
        services.AddScoped<IHeartbeatJob, HeartbeatJob>();

        return services;
    }
}
