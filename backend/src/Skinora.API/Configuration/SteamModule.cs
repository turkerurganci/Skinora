using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skinora.Steam.Application.Admin;
using Skinora.Steam.Application.Inventory;
using Skinora.Transactions.Application.Steam;

namespace Skinora.API.Configuration;

/// <summary>
/// DI registration for the Skinora.Steam module — admin read service (T63,
/// AD10), inventory query + sidecar HTTP client (T67) and the cross-module
/// port swap that replaces the forward-deferred
/// <see cref="StubSteamInventoryReader"/> with the sidecar-backed
/// <see cref="SidecarSteamInventoryReader"/>.
/// </summary>
public static class SteamModule
{
    public static IServiceCollection AddSteamModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAdminSteamBotQueryService, AdminSteamBotQueryService>();

        // T67 — sidecar inventory wiring ----------------------------------
        services.Configure<SteamSidecarOptions>(
            configuration.GetSection(SteamSidecarOptions.SectionName));

        // Single typed HttpClient drives both the inventory fetch and the
        // cache-invalidation port — same target service, same auth header.
        services.AddHttpClient<HttpSteamSidecarInventoryClient>(HttpSteamSidecarInventoryClient.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SteamSidecarOptions>>().Value;
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

        return services;
    }
}
