using Microsoft.Extensions.DependencyInjection;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Inbox;
using Skinora.Notifications.Application.Notifications;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Infrastructure.Channels;
using Skinora.Notifications.Infrastructure.DeliveryJobs;

namespace Skinora.Notifications;

/// <summary>
/// DI wiring for the Notifications module (T37 — Bildirim altyapı servisi,
/// 05 §7.1–§7.5).
/// </summary>
/// <remarks>
/// Registers:
/// <list type="bullet">
///   <item><see cref="INotificationDispatcher"/> orchestration entry point.</item>
///   <item><see cref="INotificationTemplateResolver"/> backed by the embedded
///         <c>NotificationTemplates.&lt;culture&gt;.resx</c> family
///         (<see cref="ResxNotificationTemplateResolver"/>).</item>
///   <item>One <see cref="INotificationChannelHandler"/> per
///         <see cref="Skinora.Shared.Enums.NotificationChannel"/> value (T37
///         ships stub implementations; T78 / T79 / T80 swap the concretes).</item>
///   <item><see cref="INotificationAdminAlertSink"/> default logging sink for
///         exhausted retries (05 §7.5).</item>
///   <item><see cref="NotificationDeliveryJob"/> Hangfire job class.</item>
/// </list>
/// Microsoft.Extensions.Localization is also registered here so the embedded
/// resource family can be looked up by <see cref="ResxNotificationTemplateResolver"/>.
/// </remarks>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddLocalization();

        services.AddScoped<INotificationTemplateResolver, ResxNotificationTemplateResolver>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        // T38 — platform-in-app inbox endpoints (07 §8.1–§8.4).
        services.AddScoped<INotificationInboxService, NotificationInboxService>();

        services.AddScoped<INotificationChannelHandler, EmailNotificationChannelHandler>();
        services.AddScoped<INotificationChannelHandler, TelegramNotificationChannelHandler>();
        services.AddScoped<INotificationChannelHandler, DiscordNotificationChannelHandler>();

        services.AddScoped<INotificationAdminAlertSink, LoggingNotificationAdminAlertSink>();

        // Hangfire resolves the job class per invocation through DI; scoped
        // lifetime keeps it sharing the request-scoped DbContext.
        services.AddScoped<NotificationDeliveryJob>();

        return services;
    }
}
