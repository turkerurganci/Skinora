using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Inbox;
using Skinora.Notifications.Application.Notifications;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Application.Webhooks;
using Skinora.Notifications.Infrastructure.Channels;
using Skinora.Notifications.Infrastructure.DeliveryJobs;
using Skinora.Shared.Email;

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
        var provider = configuration[$"{ResendSettings.SectionName}:{nameof(ResendSettings.Provider)}"]
            ?? ResendSettings.ProviderLogging;

        if (string.Equals(provider, ResendSettings.ProviderResend, StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<INotificationChannelHandler, ResendEmailNotificationChannelHandler>();
        }
        else
        {
            services.AddScoped<INotificationChannelHandler, EmailNotificationChannelHandler>();
        }

        services.AddScoped<INotificationChannelHandler, TelegramNotificationChannelHandler>();
        services.AddScoped<INotificationChannelHandler, DiscordNotificationChannelHandler>();

        services.AddScoped<INotificationAdminAlertSink, LoggingNotificationAdminAlertSink>();

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
