using Microsoft.Extensions.Options;
using Skinora.Shared.Telegram;

namespace Skinora.API.Configuration;

/// <summary>
/// T165 — the Telegram Bot API HttpClient registration, shared by Program.cs
/// and its test.
/// </summary>
/// <remarks>
/// <b>Token in the URL (⚠ GÜVENLİK).</b> The Bot API puts the token in the
/// request path (<c>/bot&lt;token&gt;/sendMessage</c>). IHttpClientFactory's
/// default logging handlers write that URI at Information level
/// ("Sending HTTP request POST https://api.telegram.org/bot…") — with the
/// backend's Serilog configuration straight into the console and Loki. Whoever
/// reads the logs would hold the bot. <see cref="HttpClientBuilderExtensions.RemoveAllLoggers"/>
/// drops those handlers for this client only; TelegramBotClient logs its own
/// outcome lines, which never carry the URI.
/// </remarks>
public static class TelegramTransportRegistration
{
    public static IHttpClientBuilder AddTelegramBotTransport(this IServiceCollection services) =>
        services.AddHttpClient<ITelegramBotClient, TelegramBotClient>((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<TelegramSettings>>().Value;
                client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            })
            .RemoveAllLoggers();
}
