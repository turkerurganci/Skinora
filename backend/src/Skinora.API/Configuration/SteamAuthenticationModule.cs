using MaxMind.GeoIP2;
using Microsoft.Extensions.Logging;
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

        // T30 / T83 — Access control pipeline: geo-block + age gate.
        // ChainedCountryResolver tries each registered ICountryResolver
        // (HeaderCountryResolver → MaxMindCountryResolver) and returns the
        // first non-null code. The MaxMind reader is only registered when
        // the MMDB file actually exists on disk — keeps dev/CI environments
        // running on header-only resolution. HTTP context access required
        // by HeaderCountryResolver.
        services.AddHttpContextAccessor();
        services.Configure<GeolocationSettings>(configuration.GetSection(GeolocationSettings.SectionName));
        services.AddSingleton<HeaderCountryResolver>();
        services.AddSingleton<IEnumerable<ICountryResolver>>(sp =>
        {
            var resolvers = new List<ICountryResolver>
            {
                sp.GetRequiredService<HeaderCountryResolver>(),
            };

            var geoSettings = configuration
                .GetSection(GeolocationSettings.SectionName)
                .Get<GeolocationSettings>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var moduleLogger = loggerFactory.CreateLogger("Skinora.Auth.Geolocation");

            if (geoSettings is not null
                && !string.IsNullOrWhiteSpace(geoSettings.DatabasePath)
                && File.Exists(geoSettings.DatabasePath))
            {
                try
                {
                    var reader = new DatabaseReader(geoSettings.DatabasePath);
                    var maxMindLogger = loggerFactory.CreateLogger<MaxMindCountryResolver>();
                    resolvers.Add(new MaxMindCountryResolver(reader, maxMindLogger));
                    moduleLogger.LogInformation(
                        "MaxMind GeoLite2 resolver enabled (db: {DatabasePath}).",
                        geoSettings.DatabasePath);
                }
                catch (Exception ex)
                {
                    moduleLogger.LogWarning(
                        ex,
                        "MaxMind GeoLite2 reader failed to load (path: {DatabasePath}); falling back to header-only resolution.",
                        geoSettings.DatabasePath);
                }
            }
            else
            {
                moduleLogger.LogInformation(
                    "MaxMind GeoLite2 database not configured (Geolocation:DatabasePath empty or missing); header-only resolution.");
            }

            return resolvers;
        });
        services.AddSingleton<ICountryResolver>(sp =>
            new ChainedCountryResolver(sp.GetRequiredService<IEnumerable<ICountryResolver>>()));
        services.AddScoped<IGeoBlockCheck, SettingsBasedGeoBlockCheck>();
        services.AddScoped<IAgeGateCheck, SettingsBasedAgeGateCheck>();

        // T83 — VPN/proxy supportive signal. Disabled by default
        // (VpnDetection:Enabled=false → NoOpVpnProxyDetector); production
        // sets the flag to true and the Tor exit list is fetched on first
        // login + cached for VpnDetection:CacheDurationMinutes.
        services.Configure<VpnDetectionSettings>(configuration.GetSection(VpnDetectionSettings.SectionName));
        var vpnSettings = configuration
            .GetSection(VpnDetectionSettings.SectionName)
            .Get<VpnDetectionSettings>() ?? new VpnDetectionSettings();
        if (vpnSettings.Enabled)
        {
            services.AddHttpClient<IVpnProxyDetector, TorExitNodeVpnDetector>(client =>
            {
                client.Timeout = vpnSettings.RefreshTimeout;
            });
        }
        else
        {
            services.AddSingleton<IVpnProxyDetector, NoOpVpnProxyDetector>();
        }

        // T82 — DbLoginSanctionsCheck queries User by SteamId64, then runs
        // both DefaultPayoutAddress + DefaultRefundAddress against the
        // SanctionedAddress list (Skinora.Shared.Sanctions.ISanctionedAddressLookup;
        // Platform owns the impl). Replaces the T29 NoMatchSanctionsCheck stub.
        services.AddScoped<ISanctionsCheck, DbLoginSanctionsCheck>();

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

        // T31 / WP6 — Mobile Authenticator check (07 §4.8, 08 §2.2). Real impl
        // delegates to the shared ISteamTradeHoldProbe (HttpSteamTradeHoldClient
        // registered in SteamModule → sidecar GetTradeHoldDurations). Fails closed
        // to active=false + setup guide URL when Steam is unreachable, matching
        // the conservative StubMobileAuthenticatorCheck default it replaces.
        services.AddScoped<IMobileAuthenticatorCheck, SidecarMobileAuthenticatorCheck>();

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
