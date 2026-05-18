using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Shared.Discord;
using Xunit;

namespace Skinora.Shared.Tests.Unit.Discord;

public class DiscordBotClientTests
{
    private static DiscordSettings Settings() => new()
    {
        Provider = DiscordSettings.ProviderDiscord,
        ClientId = "client",
        ClientSecret = "secret",
        BotToken = "bot-token",
        BaseUrl = "https://discord.example",
    };

    [Fact]
    public async Task CreateDmAsync_OkResponse_ReturnsChannelId()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, """{"id":"chan-1","type":1}""", null));
        var sut = BuildClient(handler);

        var result = await sut.CreateDmAsync(
            new DiscordCreateDmRequest("user-42"), CancellationToken.None);

        Assert.Equal("chan-1", result.ChannelId);
        Assert.Single(handler.Requests);
        Assert.EndsWith("users/@me/channels", handler.Requests[0].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("\"recipient_id\":\"user-42\"", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Equal("Bot", handler.Requests[0].AuthScheme);
        Assert.Equal("bot-token", handler.Requests[0].AuthParameter);
    }

    [Fact]
    public async Task SendMessageAsync_OkResponse_ReturnsMessageId()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, """{"id":"msg-9"}""", null));
        var sut = BuildClient(handler);

        var result = await sut.SendMessageAsync(
            new DiscordSendMessageRequest("chan-1", "Hello"), CancellationToken.None);

        Assert.Equal("msg-9", result.MessageId);
        Assert.EndsWith("channels/chan-1/messages", handler.Requests[0].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"Hello\"", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"allowed_mentions\":{\"parse\":[]}", handler.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateDmAsync_403_ThrowsMutualGuildRequired()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.Forbidden,
             """{"message":"Cannot create DM","code":50001}""",
             null));
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<DiscordForbiddenException>(() =>
            sut.CreateDmAsync(new DiscordCreateDmRequest("u1"), CancellationToken.None));

        Assert.Equal(DiscordForbiddenReason.MutualGuildRequired, ex.Reason);
        Assert.Equal(403, ex.HttpStatusCode);
    }

    [Fact]
    public async Task SendMessageAsync_403WithDmClosedCode_ThrowsDmClosed()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.Forbidden,
             """{"message":"Cannot send messages to this user","code":50007}""",
             null));
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<DiscordForbiddenException>(() =>
            sut.SendMessageAsync(
                new DiscordSendMessageRequest("chan-1", "x"), CancellationToken.None));

        Assert.Equal(DiscordForbiddenReason.DmClosed, ex.Reason);
        Assert.Equal(50007, ex.DiscordErrorCode);
    }

    [Fact]
    public async Task SendMessageAsync_403WithUnknownCode_ThrowsUnknownForbidden()
    {
        // Any other Discord error code on /messages 403 cannot be
        // attributed to the documented "DM closed" path — escalate to
        // admin via the Unknown reason.
        var handler = new SequenceHandler(
            (HttpStatusCode.Forbidden,
             """{"message":"Missing Permissions","code":50013}""",
             null));
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<DiscordForbiddenException>(() =>
            sut.SendMessageAsync(
                new DiscordSendMessageRequest("chan-1", "x"), CancellationToken.None));

        Assert.Equal(DiscordForbiddenReason.Unknown, ex.Reason);
    }

    [Fact]
    public async Task SendMessageAsync_401_ThrowsUnauthorized()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.Unauthorized,
             """{"message":"401: Unauthorized","code":0}""",
             null));
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<DiscordUnauthorizedException>(() =>
            sut.SendMessageAsync(
                new DiscordSendMessageRequest("chan-1", "x"), CancellationToken.None));

        Assert.Equal(401, ex.HttpStatusCode);
    }

    [Fact]
    public async Task SendMessageAsync_404_ThrowsPermanent()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.NotFound,
             """{"message":"Unknown Channel","code":10003}""",
             null));
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<DiscordPermanentException>(() =>
            sut.SendMessageAsync(
                new DiscordSendMessageRequest("missing-chan", "x"), CancellationToken.None));

        Assert.Equal(404, ex.HttpStatusCode);
        Assert.Equal(10003, ex.DiscordErrorCode);
    }

    [Fact]
    public async Task SendMessageAsync_429_RegistersRetryAfterAndThrowsTransient()
    {
        var rateLimiter = new SpyRateLimiter();
        var handler = new SequenceHandler(
            (HttpStatusCode.TooManyRequests,
             """{"message":"You are being rate limited.","retry_after":1.5,"global":false,"code":0}""",
             new[] { ("X-RateLimit-Bucket", "abc123") }));
        var sut = BuildClient(handler, rateLimiter);

        var ex = await Assert.ThrowsAsync<DiscordTransientException>(() =>
            sut.SendMessageAsync(
                new DiscordSendMessageRequest("chan-1", "x"), CancellationToken.None));

        Assert.Equal(429, ex.HttpStatusCode);
        Assert.Equal(1.5, ex.RetryAfterSeconds);
        Assert.False(ex.IsGlobal);
        Assert.Single(rateLimiter.RetryAfterCalls);
    }

    [Fact]
    public async Task SendMessageAsync_429GlobalTrue_PausesGlobalGate()
    {
        var rateLimiter = new SpyRateLimiter();
        var handler = new SequenceHandler(
            (HttpStatusCode.TooManyRequests,
             """{"message":"global limited","retry_after":2.0,"global":true,"code":0}""",
             null));
        var sut = BuildClient(handler, rateLimiter);

        var ex = await Assert.ThrowsAsync<DiscordTransientException>(() =>
            sut.SendMessageAsync(
                new DiscordSendMessageRequest("chan-1", "x"), CancellationToken.None));

        Assert.True(ex.IsGlobal);
        Assert.Single(rateLimiter.RetryAfterCalls);
        Assert.True(rateLimiter.RetryAfterCalls[0].IsGlobal);
    }

    [Fact]
    public async Task SendMessageAsync_500_ThrowsTransient()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.InternalServerError, """{"message":"oops","code":0}""", null));
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<DiscordTransientException>(() =>
            sut.SendMessageAsync(
                new DiscordSendMessageRequest("chan-1", "x"), CancellationToken.None));

        Assert.Equal(500, ex.HttpStatusCode);
    }

    [Fact]
    public async Task SendMessageAsync_TransportError_ThrowsTransient()
    {
        var handler = new ThrowingHandler(new HttpRequestException("dns failure"));
        var sut = BuildClient(handler);

        await Assert.ThrowsAsync<DiscordTransientException>(() =>
            sut.SendMessageAsync(
                new DiscordSendMessageRequest("chan-1", "x"), CancellationToken.None));
    }

    [Fact]
    public async Task SendMessageAsync_400_ThrowsPermanent()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.BadRequest,
             """{"message":"Invalid Form Body","code":50035}""",
             null));
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<DiscordPermanentException>(() =>
            sut.SendMessageAsync(
                new DiscordSendMessageRequest("chan-1", "x"), CancellationToken.None));

        Assert.Equal(400, ex.HttpStatusCode);
    }

    [Fact]
    public async Task SendMessageAsync_ResetAfterHeader_PropagatesToLimiter()
    {
        var rateLimiter = new SpyRateLimiter();
        var handler = new SequenceHandler(
            (HttpStatusCode.OK,
             """{"id":"msg-1"}""",
             new[]
             {
                 ("X-RateLimit-Bucket", "send-bucket"),
                 ("X-RateLimit-Reset-After", "0.5"),
             }));
        var sut = BuildClient(handler, rateLimiter);

        await sut.SendMessageAsync(
            new DiscordSendMessageRequest("chan-1", "x"), CancellationToken.None);

        Assert.Contains(rateLimiter.BucketRegistrations, b => b.Discord == "send-bucket");
        Assert.Contains(rateLimiter.ResetRegistrations, r => Math.Abs(r.Seconds - 0.5) < 0.0001);
    }

    [Fact]
    public void Constructor_MissingBotToken_Throws()
    {
        var settings = new DiscordSettings
        {
            Provider = DiscordSettings.ProviderDiscord,
            ClientId = "c",
            ClientSecret = "s",
            BotToken = "",
        };
        var httpClient = new HttpClient(new SequenceHandler());

        Assert.Throws<InvalidOperationException>(() => new DiscordBotClient(
            httpClient,
            Options.Create(settings),
            new SpyRateLimiter(),
            NullLogger<DiscordBotClient>.Instance));
    }

    private static DiscordBotClient BuildClient(
        HttpMessageHandler handler, IDiscordRateLimiter? rateLimiter = null)
    {
        var httpClient = new HttpClient(handler);
        return new DiscordBotClient(
            httpClient,
            Options.Create(Settings()),
            rateLimiter ?? new SpyRateLimiter(),
            NullLogger<DiscordBotClient>.Instance);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();
        private readonly Queue<(HttpStatusCode Status, string Body, (string Name, string Value)[]? Headers)> _queue = new();

        public SequenceHandler(params (HttpStatusCode Status, string Body, (string Name, string Value)[]? Headers)[] responses)
        {
            foreach (var r in responses) _queue.Enqueue(r);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            // Falls back to DefaultRequestHeaders on the typed client so
            // the per-request header is captured when the client set it
            // on the message instead of the default headers.
            var authHeader = request.Headers.Authorization
                ?? request.Options
                    .OfType<KeyValuePair<string, object?>>()
                    .Select(kv => kv.Value as AuthenticationHeaderValue)
                    .FirstOrDefault();

            Requests.Add(new RecordedRequest(
                request.RequestUri!,
                body,
                authHeader?.Scheme,
                authHeader?.Parameter));

            if (_queue.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            var (status, responseBody, headers) = _queue.Dequeue();
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };

            if (headers is not null)
            {
                foreach (var (name, value) in headers)
                {
                    response.Headers.TryAddWithoutValidation(name, value);
                }
            }

            return response;
        }
    }

    private sealed record RecordedRequest(
        Uri Uri,
        string Body,
        string? AuthScheme,
        string? AuthParameter);

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;
        public ThrowingHandler(Exception ex) => _ex = ex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _ex;
    }

    private sealed class SpyRateLimiter : IDiscordRateLimiter
    {
        public List<(string Bucket, double Seconds, bool IsGlobal)> RetryAfterCalls { get; } = new();
        public List<(string Bucket, string Discord)> BucketRegistrations { get; } = new();
        public List<(string Bucket, double Seconds)> ResetRegistrations { get; } = new();

        public Task WaitAsync(string bucket, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public void RegisterRetryAfter(string bucket, double seconds, bool isGlobal)
            => RetryAfterCalls.Add((bucket, seconds, isGlobal));

        public void RegisterBucket(string bucket, string discordBucket)
            => BucketRegistrations.Add((bucket, discordBucket));

        public void RegisterReset(string bucket, double resetAfterSeconds)
            => ResetRegistrations.Add((bucket, resetAfterSeconds));
    }
}
