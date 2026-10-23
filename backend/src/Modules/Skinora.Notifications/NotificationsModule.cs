using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Inbox;
using Skinora.Notifications.Application.Notifications;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Application.Webhooks;
using Skinora.Notifications.Infrastructure.Channels;
using Skinora.Notifications.Infrastructure.DeliveryJobs;
using Skinora.Shared.Discord;
using Skinora.Shared.Email;
using Skinora.Shared.Enums;
using Skinora.Shared.Notifications;
using StackExchange.Redis;

namespace Skinora.Notifications;

/// <summary>
/// DI wiring for the Notifications module (T37 — Bildirim altyapı servisi,
/// 05 §7.1–§7.5; T78 added Resend email transport + deferred-tier
/// delivery job + webhook handler).
/// </summary>
/// <remarks>
/// Registers:
/// <list type="bullet">
///   <item><see cref="INotificationDispatcher"/> orchestration entry point.</item>
///   <item><see cref="INotificationTemplateResolver"/> backed by the embedded
///         <c>NotificationTemplates.&lt;culture&gt;.resx</c> family
///         (<see cref="ResxNotificationTemplateResolver"/>).</item>
///   <item>One <see cref="INotificationChannelHandler"/> per
///         <see cref="Skinora.Shared.Enums.NotificationChannel"/> value. T78
///         swaps the EMAIL stub for the Resend-backed handler when
///         <c>Resend:Provider</c> is <c>resend</c>; Telegram + Discord stay
///         on their T37 stubs until T79 / T80.</item>
///   <item><see cref="INotificationAdminAlertSink"/> default logging sink for
///         exhausted retries (05 §7.5).</item>
///   <item><see cref="NotificationDeliveryJob"/> + the T78
///         <see cref="DeferredNotificationDeliveryJob"/> Hangfire job classes.</item>
///   <item><see cref="IResendWebhookHandler"/> + <see cref="IEmailHtmlRenderer"/>
///         (always registered so the webhook endpoint stays callable in
///         logging-mode environments — important for staging tests).</item>
/// </list>
/// Microsoft.Extensions.Localization is also registered here so the embedded
/// resource family can be looked up by <see cref="ResxNotificationTemplateResolver"/>.
/// </remarks>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddLocalization();

        services.AddScoped<INotificationTemplateResolver, ResxNotificationTemplateResolver>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        // T38 — platform-in-app inbox endpoints (07 §8.1–§8.4).
        services.AddScoped<INotificationInboxService, NotificationInboxService>();

        // T78 — HTML wrapper rendering used by both the notification email
        // channel handler and the Users-module verification email sender.
        services.AddSingleton<IEmailHtmlRenderer, EmailHtmlRenderer>();

        // T78 — Resend channel handler swap. The provider switch keeps the
        // T37 logging stub registered for tests / dev so CI never reaches
        // the network. Telegram + Discord stay on stub handlers until
        // T79 / T80 land.
        //
        // T165 — the switch reads the evaluated channel state, not the bare
        // Provider string: a real provider with missing / placeholder /
        // malformed secrets (Misconfigured) keeps the stub registered, and the
        // dispatcher never creates a delivery on it (INotificationChannelAvailability).
        var channels = NotificationChannelAvailability.FromConfiguration(key => configuration[key]);

        if (channels.Get(NotificationChannel.EMAIL).IsLive)
        {
            services.AddScoped<INotificationChannelHandler, ResendEmailNotificationChannelHandler>();
        }
        else
        {
            services.AddScoped<INotificationChannelHandler, EmailNotificationChannelHandler>();
        }

        // T79 — Telegram channel handler swap. Mirrors the T78 email
        // provider switch: <c>telegram</c> wires the real Bot API
        // handler, anything else (default <c>logging</c>) keeps the
        // T37 stub so CI never reaches the network.
        if (channels.Get(NotificationChannel.TELEGRAM).IsLive)
        {
            services.AddScoped<INotificationChannelHandler, TelegramNotificationChannelHandler>();
        }
        else
        {
            services.AddScoped<INotificationChannelHandler, LoggingTelegramNotificationChannelHandler>();
        }

        // T80 — Discord channel handler swap. Mirrors the T79
        // Telegram + T78 Resend provider switches: <c>discord</c>
        // wires the real Bot API handler + Redis DM channel cache;
        // anything else (default <c>logging</c>) keeps the T37 stub
        // so CI never reaches the network. The DM channel cache is
        // registered unconditionally — the stub doesn't use it, and
        // tests rely on it being override-able to inject the
        // in-memory cache.
        services.AddSingleton<IDiscordDmChannelCache>(sp =>
            new RedisDiscordDmChannelCache(
                sp.GetRequiredService<IConnectionMultiplexer>(), keyPrefix: "skinora"));

        if (channels.Get(NotificationChannel.DISCORD).IsLive)
        {
            services.AddScoped<INotificationChannelHandler, DiscordNotificationChannelHandler>();
        }
        else
        {
            services.AddScoped<INotificationChannelHandler, LoggingDiscordNotificationChannelHandler>();
        }

        services.TryAddScoped<INotificationAdminAlertSink, LoggingNotificationAdminAlertSink>();

        // T165 — money-alarm observer; the API host registers the Prometheus
        // implementation first, so this default only serves other hosts/tests.
        services.TryAddSingleton<IAdminMoneyAlarmObserver>(NullAdminMoneyAlarmObserver.Instance);

        // Hangfire resolves the job class per invocation through DI; scoped
        // lifetime keeps it sharing the request-scoped DbContext.
        services.AddScoped<NotificationDeliveryJob>();

        // T78 — deferred-tier delivery job (30 dk / 1 sa / 4 sa). Always
        // registered so a row already in DEFERRED before the swap still
        // has a runnable job class to dispatch to.
        services.AddScoped<DeferredNotificationDeliveryJob>();

        // T78 — inbound Resend webhook handler. Registered regardless of
        // provider so staging environments running the logging stub can
        // still receive smoke-test events from the Resend dashboard.
        services.AddScoped<IResendWebhookHandler, ResendWebhookHandler>();

        return services;
    }
}
