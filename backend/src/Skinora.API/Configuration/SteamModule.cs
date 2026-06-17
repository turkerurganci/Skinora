using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Skinora.Steam.Application.Admin;
using Skinora.Steam.Application.BotSelection;
using Skinora.Steam.Application.Dispatch;
using Skinora.Steam.Application.Inventory;
using Skinora.Steam.Application.Recovery;
using Skinora.Steam.Application.Webhooks;
using Skinora.Shared.Steam;
using Skinora.Transactions.Application.Steam;
using Skinora.Users.Application.Settings;

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

        // T103b-2 — S18 recovery queue read + triage service (AD25/AD26).
        services.AddScoped<IAdminBotRecoveryService, AdminBotRecoveryService>();

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

        // T68 — inbound webhook handler.
        services.AddScoped<ISteamWebhookHandler, SteamWebhookHandler>();

        // T69 — capacity-based bot selector. Consumed by the T106a dispatch job.
        services.AddScoped<IBotSelectionService, SqlBotSelectionService>();

        // T106a — escrow trade-offer dispatch engine (formalises T69-K1).
        // The dispatch client shares SteamSidecarOptions with the inventory
        // client (same container, same X-Internal-Key) but is its own typed
        // HttpClient so the trade endpoint carries a longer timeout — the
        // sidecar may run a 5/15/45s internal retry before answering (08 §2.7).
        services.AddHttpClient<HttpTradeOfferDispatchClient>(
            HttpTradeOfferDispatchClient.HttpClientName, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<SteamSidecarOptions>>().Value;
                if (!string.IsNullOrWhiteSpace(options.BaseUrl))
                {
                    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                }
                var seconds = options.TimeoutSeconds <= 0 ? 30 : options.TimeoutSeconds * 3;
                client.Timeout = TimeSpan.FromSeconds(seconds);
            });
        services.AddScoped<ITradeOfferDispatchClient>(sp =>
            sp.GetRequiredService<HttpTradeOfferDispatchClient>());

        // Per-minute dispatch job (escrow + delivery legs) + its registrar.
        services.AddScoped<TradeOfferDispatchJob>();
        services.AddHostedService<TradeOfferDispatchJobRegistrar>();

        // Refund leg — MediatR notification handler consumes the outbox
        // ItemRefundToSellerRequestedEvent (timeout / user-cancel / admin-cancel
        // publishers) and dispatches RETURN_TO_SELLER.
        services.AddScoped<ItemRefundDispatchConsumer>();
        services.AddScoped<MediatR.INotificationHandler<
            Skinora.Shared.Events.ItemRefundToSellerRequestedEvent>>(sp =>
            sp.GetRequiredService<ItemRefundDispatchConsumer>());

        // T103b-2 — bot restriction → recovery queue materialisation + auto-hold.
        // Shared materialiser drives both the event-driven sweep below and the
        // boundary-race safety net in SteamWebhookHandler.AcceptEscrowAsync (F3).
        services.AddScoped<IBotRecoveryMaterialiser, BotRecoveryMaterialiser>();

        // Consumes the outbox BotRestrictedEvent published by the webhook handler.
        services.AddScoped<BotRestrictionRecoveryConsumer>();
        services.AddScoped<MediatR.INotificationHandler<
            Skinora.Shared.Events.BotRestrictedEvent>>(sp =>
            sp.GetRequiredService<BotRestrictionRecoveryConsumer>());

        return services;
    }
}
