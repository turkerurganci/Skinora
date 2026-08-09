using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Skinora.Steam.Application.Inventory;
using Skinora.Shared.Steam;
using Skinora.Transactions.Application.Steam;
using Skinora.Users.Application.Settings;

namespace Skinora.API.Configuration;

/// <summary>
/// DI registration for the Skinora.Steam module.
/// <para>
/// As of v3.0 this module is <b>read-only</b>: the platform no longer runs
/// Steam bot accounts, sends trade offers or receives Steam webhooks
/// (02 §15, 05 §3.2). What remains is inventory reading — now the backbone of
/// delivery verification (02 §9.2) — and the trade-hold probe used to confirm
/// a user's Mobile Authenticator is active.
/// </para>
/// </summary>
public static class SteamModule
{
    public static IServiceCollection AddSteamModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        // T67 — sidecar inventory wiring ----------------------------------
        services.Configure<SteamSidecarOptions>(
            configuration.GetSection(SteamSidecarOptions.SectionName));

        // Single typed HttpClient drives both the inventory fetch and the
        // cache-invalidation port — same target service, same auth header.
        services.AddHttpClient<HttpSteamSidecarInventoryClient>(HttpSteamSidecarInventoryClient.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<SteamSidecarOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            }
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds <= 0 ? 30 : options.TimeoutSeconds);
        });

        // Bridge the typed client to both interfaces so consumers stay
        // decoupled from the concrete implementation.
        services.AddScoped<ISteamSidecarInventoryClient>(sp =>
            sp.GetRequiredService<HttpSteamSidecarInventoryClient>());
        services.AddScoped<ISteamInventoryQueryService, SteamInventoryQueryService>();

        // Swap the stub `ISteamInventoryReader` registered by Transactions
        // module (TryAddScoped → first wins) with the sidecar-backed reader
        // and replace the no-op cache invalidator.
        services.Replace(ServiceDescriptor.Scoped<ISteamInventoryReader, SidecarSteamInventoryReader>());
        services.Replace(ServiceDescriptor.Scoped<ISteamInventoryCacheInvalidator>(sp =>
            sp.GetRequiredService<HttpSteamSidecarInventoryClient>()));

        // WP6 — sidecar trade-hold / Mobile Authenticator probe (08 §2.2).
        // Own typed client (separate timeout from inventory pagination) sharing
        // SteamSidecarOptions + the X-Internal-Key header. Bridged to the shared
        // ISteamTradeHoldProbe port that both checkers below consume.
        services.AddHttpClient<HttpSteamTradeHoldClient>(
            HttpSteamTradeHoldClient.HttpClientName, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<SteamSidecarOptions>>().Value;
                if (!string.IsNullOrWhiteSpace(options.BaseUrl))
                {
                    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                }
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds <= 0 ? 30 : options.TimeoutSeconds);
            });
        services.AddScoped<ISteamTradeHoldProbe>(sp =>
            sp.GetRequiredService<HttpSteamTradeHoldClient>());

        // Swap the stub `ITradeHoldChecker` registered by Users module
        // (TryAddScoped → first wins) with the sidecar-backed checker (U17
        // trade-URL save). The IMobileAuthenticatorCheck swap (A7) lives in
        // SteamAuthenticationModule — Skinora.Steam does not reference Skinora.Auth.
        services.Replace(ServiceDescriptor.Scoped<ITradeHoldChecker, SidecarTradeHoldChecker>());

        return services;
    }
}
