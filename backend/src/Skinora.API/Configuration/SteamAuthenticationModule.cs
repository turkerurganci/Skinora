using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Configuration;

namespace Skinora.API.Configuration;

/// <summary>
/// DI registration for T29 — Steam OpenID authentication services.
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

        services.AddScoped<IAccessTokenGenerator, AccessTokenGenerator>();
        services.AddScoped<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<ILoginAuditService, LoginAuditService>();

        // Stub hooks — T30/T82/T83 replace with real implementations.
        services.AddSingleton<IGeoBlockCheck, AllowAllGeoBlockCheck>();
        services.AddSingleton<ISanctionsCheck, NoMatchSanctionsCheck>();

        services.AddScoped<ISteamAuthenticationPipeline, SteamAuthenticationPipeline>();

        return services;
    }
}
