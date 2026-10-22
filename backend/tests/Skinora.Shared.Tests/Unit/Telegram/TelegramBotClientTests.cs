using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Shared.Telegram;
using Xunit;

namespace Skinora.Shared.Tests.Unit.Telegram;

public class TelegramBotClientTests
{
    private static TelegramSettings Settings() => new()
    {
        Provider = TelegramSettings.ProviderTelegram,
        BotToken = "111:test-token",
        BaseUrl = "https://api.telegram.org",
    };

    [Fact]
    public async Task SendMessageAsync_OkResponse_ReturnsMessageId()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """{"ok":true,"result":{"message_id":4242}}""");
        var sut = BuildClient(handler);

        var result = await sut.SendMessageAsync(
            new TelegramSendMessageRequest("12345", "*Hello*\n\nWorld"),
            CancellationToken.None);

        Assert.Equal(4242, result.MessageId);
        Assert.Single(handler.Requests);
        Assert.EndsWith("/sendMessage", handler.Requests[0].requestUri);
        Assert.Contains("\"parse_mode\":\"MarkdownV2\"", handler.Requests[0].body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMessageAsync_429WithRetryAfter_ThrowsTransientWithRetryAfter()
    {
        var handler = new StubHandler(
            HttpStatusCode.TooManyRequests,
            """{"ok":false,"error_code":429,"description":"Too Many Requests: retry after 5","parameters":{"retry_after":5}}""");
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<TelegramTransientException>(() =>
            sut.SendMessageAsync(
                new TelegramSendMessageRequest("12345", "x"),
                CancellationToken.None));

        Assert.Equal(429, ex.HttpStatusCode);
        Assert.Equal(429, ex.TelegramErrorCode);
        Assert.Equal(5, ex.RetryAfterSeconds);
    }

    [Fact]
    public async Task SendMessageAsync_500_ThrowsTransient()
    {
        var handler = new StubHandler(
            HttpStatusCode.InternalServerError,
            """{"ok":false,"error_code":500,"description":"Internal Server Error"}""");
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<TelegramTransientException>(() =>
            sut.SendMessageAsync(
                new TelegramSendMessageRequest("12345", "x"),
                CancellationToken.None));

        Assert.Equal(500, ex.HttpStatusCode);
    }

    [Fact]
    public async Task SendMessageAsync_403BotBlocked_ThrowsForbiddenWithReason()
    {
        var handler = new StubHandler(
            HttpStatusCode.Forbidden,
            """{"ok":false,"error_code":403,"description":"Forbidden: bot was blocked by the user"}""");
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<TelegramForbiddenException>(() =>
            sut.SendMessageAsync(
                new TelegramSendMessageRequest("12345", "x"),
                CancellationToken.None));

        Assert.Equal(TelegramForbiddenReason.BotBlockedByUser, ex.Reason);
        Assert.Equal(403, ex.HttpStatusCode);
    }

    [Theory]
    [InlineData("Forbidden: user is deactivated", TelegramForbiddenReason.UserDeactivated)]
    [InlineData("Forbidden: bot can't send messages to bots", TelegramForbiddenReason.CannotMessageBots)]
    [InlineData("Forbidden: bot can't initiate conversation with a user", TelegramForbiddenReason.CannotInitiateConversation)]
    [InlineData("Forbidden: weird new reason", TelegramForbiddenReason.Unknown)]
    public async Task SendMessageAsync_403_ClassifiesReasonFromDescription(
        string description, TelegramForbiddenReason expected)
    {
        var payload = $$"""{"ok":false,"error_code":403,"description":"{{description}}"}""";
        var handler = new StubHandler(HttpStatusCode.Forbidden, payload);
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<TelegramForbiddenException>(() =>
            sut.SendMessageAsync(
                new TelegramSendMessageRequest("12345", "x"),
                CancellationToken.None));

        Assert.Equal(expected, ex.Reason);
    }

    [Fact]
    public async Task SendMessageAsync_400ChatNotFound_ThrowsPermanent()
    {
        var handler = new StubHandler(
            HttpStatusCode.BadRequest,
            """{"ok":false,"error_code":400,"description":"Bad Request: chat not found"}""");
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<TelegramPermanentException>(() =>
            sut.SendMessageAsync(
                new TelegramSendMessageRequest("99999", "x"),
                CancellationToken.None));

        Assert.Equal(400, ex.HttpStatusCode);
    }

    [Fact]
    public async Task SendMessageAsync_TransportError_ThrowsTransient()
    {
        var handler = new ThrowingHandler(new HttpRequestException("dns failure"));
        var sut = BuildClient(handler);

        await Assert.ThrowsAsync<TelegramTransientException>(() =>
            sut.SendMessageAsync(
                new TelegramSendMessageRequest("12345", "x"),
                CancellationToken.None));
    }

    [Fact]
    public async Task SetWebhookAsync_OkResponse_ReturnsWithoutThrowing()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """{"ok":true,"result":true,"description":"Webhook was set"}""");
        var sut = BuildClient(handler);

        await sut.SetWebhookAsync(
            new TelegramSetWebhookRequest(
                Url: "https://example.com/webhook",
                SecretToken: "super-secret",
                MaxConnections: 40,
                AllowedUpdates: new[] { "message" },
                DropPendingUpdates: true),
            CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.EndsWith("/setWebhook", handler.Requests[0].requestUri);
        Assert.Contains("\"secret_token\":\"super-secret\"", handler.Requests[0].body, StringComparison.Ordinal);
        Assert.Contains("\"max_connections\":40", handler.Requests[0].body, StringComparison.Ordinal);
    }

    // ---- T165 — the documented Bot API contract (core.telegram.org/bots/api) ----

    [Fact]
    public async Task SendMessageAsync_PostsToBotTokenPath_WithSnakeCaseJson()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":7}}""");
        var sut = BuildClient(handler);

        await sut.SendMessageAsync(
            new TelegramSendMessageRequest("-1001234", "*T*", DisableNotification: true),
            CancellationToken.None);

        var (uri, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.telegram.org/bot111:test-token/sendMessage", uri);
        using var json = System.Text.Json.JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("-1001234", root.GetProperty("chat_id").GetString());
        Assert.Equal("*T*", root.GetProperty("text").GetString());
        Assert.Equal("MarkdownV2", root.GetProperty("parse_mode").GetString());
        Assert.True(root.GetProperty("disable_notification").GetBoolean());
    }

    [Fact]
    public async Task SetWebhookAsync_Body_MatchesTheDocumentedParameters()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"ok":true,"result":true,"description":"Webhook was set"}""");
        var sut = BuildClient(handler);

        await sut.SetWebhookAsync(
            new TelegramSetWebhookRequest(
                Url: "https://skinora.com/api/v1/webhooks/telegram",
                SecretToken: "test-webhook_SECRET_123",
                MaxConnections: 40,
                AllowedUpdates: null,
                DropPendingUpdates: true),
            CancellationToken.None);

        using var json = System.Text.Json.JsonDocument.Parse(Assert.Single(handler.Requests).body);
        var root = json.RootElement;
        Assert.Equal("https://skinora.com/api/v1/webhooks/telegram", root.GetProperty("url").GetString());
        Assert.Equal("test-webhook_SECRET_123", root.GetProperty("secret_token").GetString());
        Assert.Equal(40, root.GetProperty("max_connections").GetInt32());
        Assert.Equal(["message"], root.GetProperty("allowed_updates").EnumerateArray().Select(e => e.GetString()));
        Assert.True(root.GetProperty("drop_pending_updates").GetBoolean());
    }

    [Theory]
    // Documented failure envelope: {"ok":false,"error_code":…,"description":…}.
    [InlineData(HttpStatusCode.Unauthorized, """{"ok":false,"error_code":401,"description":"Unauthorized"}""")]
    [InlineData(HttpStatusCode.NotFound, """{"ok":false,"error_code":404,"description":"Not Found"}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"ok":false,"error_code":400,"description":"Bad Request: can't parse entities"}""")]
    public async Task SendMessageAsync_RevokedTokenOrBadRequest_IsPermanent(HttpStatusCode status, string body)
    {
        // A revoked token (BotFather /revoke) answers 401 — permanent: retrying
        // cannot help, the delivery fails and the exhausted-delivery alarm fires.
        var sut = BuildClient(new StubHandler(status, body));

        var ex = await Assert.ThrowsAsync<TelegramPermanentException>(() =>
            sut.SendMessageAsync(new TelegramSendMessageRequest("1", "x"), CancellationToken.None));

        Assert.Equal((int)status, ex.HttpStatusCode);
        Assert.DoesNotContain("test-token", ex.Message);
    }

    [Fact]
    public async Task SendMessageAsync_TransportError_DoesNotPutTheTokenInTheMessage()
    {
        var sut = BuildClient(new ThrowingHandler(new HttpRequestException("Connection refused (api.telegram.org:443)")));

        var ex = await Assert.ThrowsAsync<TelegramTransientException>(() =>
            sut.SendMessageAsync(new TelegramSendMessageRequest("1", "x"), CancellationToken.None));

        Assert.DoesNotContain("test-token", ex.Message);
    }

    [Fact]
    public void Constructor_MissingBotToken_Throws()
    {
        var settings = new TelegramSettings { Provider = TelegramSettings.ProviderTelegram, BotToken = "" };
        var httpClient = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));

        Assert.Throws<InvalidOperationException>(() => new TelegramBotClient(
            httpClient,
            Options.Create(settings),
            NullLogger<TelegramBotClient>.Instance));
    }

    private static TelegramBotClient BuildClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new TelegramBotClient(
            httpClient,
            Options.Create(Settings()),
            NullLogger<TelegramBotClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<(string requestUri, string body)> Requests { get; } = new();
        private readonly HttpStatusCode _status;
        private readonly string _responseBody;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _responseBody = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add((request.RequestUri!.AbsoluteUri, requestBody));

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;
        public ThrowingHandler(Exception ex) => _ex = ex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _ex;
    }
}
