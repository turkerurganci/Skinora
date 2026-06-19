using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Application.Heartbeat;
using Skinora.Platform.Application.Settings;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Sanctions;

namespace Skinora.Platform;

/// <summary>
/// DI wiring for the Skinora.Platform module — system settings (T41), the
/// central audit logger + query service (T42, 07 §9.19, 09 §18.6), the
/// platform heartbeat (T47) ve sanctions adres listesi lookup'u (T82, 06
/// §3.25). <see cref="HeartbeatOptions"/> binding lives in the API host
/// (Program.cs) so this assembly does not need the
/// Microsoft.Extensions.Configuration.Binder dependency that
/// <c>Microsoft.NET.Sdk.Web</c> ships out of the box.
/// </summary>
public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ISystemSettingsService, SystemSettingsService>();

        // WP14 — default no-op settings-change propagator. The API host replaces
        // this with CronSettingChangePropagator (cron job re-registration); the
        // TryAdd keeps the bootstrap, unit tests, and non-API hosts resolving.
        services.TryAddSingleton<ISettingChangePropagator>(NoOpSettingChangePropagator.Instance);
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();

        // T47 — platform heartbeat (self-rescheduling job target). Caller
        // (Program.cs) binds HeartbeatOptions before this is invoked.
        services.AddScoped<IHeartbeatJob, HeartbeatJob>();

        // T82 — sanctions list lookup port. Single-row AsNoTracking read keyed
        // on the case-sensitive address; consumed by DbWalletSanctionsCheck
        // (Users module), DbLoginSanctionsCheck (Auth module) ve admin
        // retroaktif scan (Skinora.API/Services/AdminSanctions).
        services.AddScoped<ISanctionedAddressLookup, SanctionedAddressLookup>();

        return services;
    }
}
