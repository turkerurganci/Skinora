using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skinora.API.Configuration;
using Skinora.Shared.Telegram;

namespace Skinora.API.Tests.Unit.Notifications;

/// <summary>
/// T165 (⚠ GÜVENLİK) — the Bot API token sits in the request path; the default
/// IHttpClientFactory loggers would write it to the log (and Loki). The
/// production registration removes them; the contrast case proves the test
/// would see the token if they were there.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TelegramTransportRegistrationTests
{
    private const string Token = "123456789:AAH-secret-token-value-0123456789abcd";

    [Fact]
    public async Task ProductionRegistration_DoesNotLogTheRequestUri()
    {
        var logs = await SendThroughAsync(services => services.AddTelegramBotTransport());

        Assert.DoesNotContain(logs, l => l.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DefaultRegistration_WouldLogTheToken_Contrast()
    {
        var logs = await SendThroughAsync(services =>
            services.AddHttpClient<ITelegramBotClient, TelegramBotClient>());

        Assert.Contains(logs, l => l.Contains(Token, StringComparison.Ordinal));
    }

    private static async Task<List<string>> SendThroughAsync(Func<IServiceCollection, IHttpClientBuilder> register)
    {
        var sink = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(sink));
        services.Configure<TelegramSettings>(s =>
        {
            s.Provider = TelegramSettings.ProviderTelegram;
            s.BotToken = Token;
        });
        register(services).ConfigurePrimaryHttpMessageHandler(() => new OkHandler());

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITelegramBotClient>();
        await client.SendMessageAsync(new TelegramSendMessageRequest("42", "hi"), CancellationToken.None);
        return sink.Lines;
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true,"result":{"message_id":1}}""", Encoding.UTF8, "application/json"),
            });
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner.Lines) owner.Lines.Add($"{category}: {formatter(state, exception)}");
            }
        }
    }
}
