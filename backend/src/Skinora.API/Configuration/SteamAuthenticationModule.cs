using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Application.TosAcceptance;
using Skinora.Auth.Configuration;

namespace Skinora.API.Configuration;

/// <summary>
/// DI registration for T29/T30 — Steam OpenID authentication, access control
/// pipeline (geo-block, age gate, sanctions), and ToS acceptance.
/// </summary>
public static class SteamAuthenticationModule
{
    public static IServiceCollection AddSteamAuthenticationModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SteamOpenIdSettings.SectionName);
        services.Configure<SteamOpenIdSettings>(section);

        var settings = section.Get<SteamOpenIdSettings>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{SteamOpenIdSettings.SectionName}' is missing.");

        // Typed HttpClients for OpenID verification + Steam Web API calls.
        services.AddHttpClient<ISteamOpenIdValidator, SteamOpenIdValidator>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient<ISteamProfileClient, SteamProfileClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddSingleton<IReturnUrlValidator>(new ReturnUrlValidator(settings.DefaultReturnPath));
        services.TryAddSingletonTimeProvider();

        services.AddScoped<IAccessTokenGenerator, AccessTokenGenerator>();
        services.AddScoped<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<ILoginAuditService, LoginAuditService>();

        // T30 — Access control pipeline: geo-block (SystemSetting-backed) +
        // age gate (Steam account-age threshold). HTTP context access is
        // required by HeaderCountryResolver.
        services.AddHttpContextAccessor();
        services.AddSingleton<ICountryResolver, HeaderCountryResolver>();
        services.AddScoped<IGeoBlockCheck, SettingsBasedGeoBlockCheck>();
        services.AddScoped<IAgeGateCheck, SettingsBasedAgeGateCheck>();

        // Stub hook — T82 replaces with real sanctions integration.
        services.AddSingleton<ISanctionsCheck, NoMatchSanctionsCheck>();

        services.AddScoped<ISteamAuthenticationPipeline, SteamAuthenticationPipeline>();

        // T30 — ToS acceptance + 18+ self-attestation (07 §4.4).
        services.AddScoped<ITosAcceptanceService, TosAcceptanceService>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);
    }
}
