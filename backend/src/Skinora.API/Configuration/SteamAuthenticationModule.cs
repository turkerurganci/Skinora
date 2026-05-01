using Skinora.Auth.Application.MobileAuthenticator;
using Skinora.Auth.Application.ReAuthentication;
using Skinora.Auth.Application.Session;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Application.TosAcceptance;
using Skinora.Auth.Configuration;
using StackExchange.Redis;

namespace Skinora.API.Configuration;

/// <summary>
/// DI registration for T29/T30/T31/T32 — Steam OpenID authentication, access
/// control pipeline (geo-block, age gate, sanctions), ToS acceptance, Steam
/// re-verify + Mobile Authenticator check, and refresh-token session
/// management (rotate, revoke, /auth/me, cleanup job).
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

        // T40 — Resolves AdminUserRole → AdminRole → AdminRolePermission so
        // AccessTokenGenerator can stamp role + permission claims into JWTs.
        services.AddScoped<IAdminAuthorityResolver, AdminAuthorityResolver>();
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

        // T31 — Steam re-verify (07 §4.6–§4.7). Data Protection backs the state
        // cookie; Redis (shared with rate limiting) stores the single-use
        // reAuthToken payload.
        services.AddDataProtection();
        services.AddSingleton<IReAuthStateProtector, ReAuthStateProtector>();
        services.AddSingleton<IReAuthTokenStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisReAuthTokenStore(redis, keyPrefix: "skinora");
        });
        services.AddScoped<IReAuthPipeline, ReAuthPipeline>();
        services.AddScoped<IReAuthTokenValidator, ReAuthTokenValidator>();

        // T31 — Mobile Authenticator check (07 §4.8, 08 §2.2). Stub returns
        // active=false + setup guide URL; Steam sidecar impl arrives with T64–T69.
        services.AddScoped<IMobileAuthenticatorCheck, StubMobileAuthenticatorCheck>();

        // T32 — Session management: refresh-token rotation, logout, /auth/me,
        // Redis-cached DB source of truth (05 §6.1), daily cleanup job.
        services.AddSingleton<IRefreshTokenCache>(sp =>
            new RedisRefreshTokenCache(
                sp.GetRequiredService<IConnectionMultiplexer>(), keyPrefix: "skinora"));
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<RefreshTokenCleanupJob>();
        services.AddHostedService<RefreshTokenCleanupJobRegistrar>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);
    }
}
