using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Shared.Discord;
using Xunit;

namespace Skinora.Shared.Tests.Unit.Discord;

public class DiscordOAuthClientTests
{
    private static DiscordSettings Settings() => new()
    {
        Provider = DiscordSettings.ProviderDiscord,
        ClientId = "test-client-id",
        ClientSecret = "test-client-secret",
        BaseUrl = "https://discord.example",
        RedirectUri = "https://app.example/callback",
    };

    [Fact]
    public async Task ExchangeAsync_BlankCode_ReturnsNull()
    {
        var handler = new SequenceHandler();
        var sut = BuildClient(handler);

        var result = await sut.ExchangeAsync("", CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExchangeAsync_TokenOkUsersMeOk_ReturnsProfile()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, """{"access_token":"abc","token_type":"Bearer","expires_in":604800,"scope":"identify"}"""),
            (HttpStatusCode.OK, """{"id":"123456789","username":"alice","global_name":"Alice"}"""));
        var sut = BuildClient(handler);

        var profile = await sut.ExchangeAsync("code-123", CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal("123456789", profile!.DiscordUserId);
        Assert.Equal("Alice", profile.Username);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("oauth2/token", handler.Requests[0].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("grant_type=authorization_code", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("code=code-123", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains(
            "application/x-www-form-urlencoded",
            handler.Requests[0].ContentType ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("users/@me", handler.Requests[1].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("Bearer", handler.Requests[1].AuthScheme);
        Assert.Equal("abc", handler.Requests[1].AuthParameter);
    }

    [Fact]
    public async Task ExchangeAsync_UsernameOnlyWithoutGlobalName_FallsBackToUsername()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, """{"access_token":"abc","token_type":"Bearer"}"""),
            (HttpStatusCode.OK, """{"id":"123","username":"legacy_user"}"""));
        var sut = BuildClient(handler);

        var profile = await sut.ExchangeAsync("code", CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal("legacy_user", profile!.Username);
    }

    [Fact]
    public async Task ExchangeAsync_TokenInvalidGrant_ThrowsInvalidGrant()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"code already used"}"""));
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<DiscordOAuthExchangeException>(() =>
            sut.ExchangeAsync("stale-code", CancellationToken.None));

        Assert.Equal(DiscordOAuthFailureReason.InvalidGrant, ex.Reason);
        Assert.Equal(400, ex.HttpStatusCode);
    }

    [Fact]
    public async Task ExchangeAsync_Token4xxWithoutInvalidGrantKeyword_TreatedAsInvalidGrant()
    {
        // A 4xx against /oauth2/token with no parseable body almost
        // always means a stale code — we surface the documented
        // ?reason=expired redirect.
        var handler = new SequenceHandler(
            (HttpStatusCode.BadRequest, ""));
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<DiscordOAuthExchangeException>(() =>
            sut.ExchangeAsync("stale-code", CancellationToken.None));

        Assert.Equal(DiscordOAuthFailureReason.InvalidGrant, ex.Reason);
    }

    [Fact]
    public async Task ExchangeAsync_Token5xx_ThrowsTokenExchangeFailed()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.InternalServerError, "internal error"));
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<DiscordOAuthExchangeException>(() =>
            sut.ExchangeAsync("code", CancellationToken.None));

        Assert.Equal(DiscordOAuthFailureReason.TokenExchangeFailed, ex.Reason);
        Assert.Equal(500, ex.HttpStatusCode);
    }

    [Fact]
    public async Task ExchangeAsync_TokenTransportFailure_ThrowsTokenExchangeFailed()
    {
        var handler = new ThrowingHandler(new HttpRequestException("dns failure"));
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<DiscordOAuthExchangeException>(() =>
            sut.ExchangeAsync("code", CancellationToken.None));

        Assert.Equal(DiscordOAuthFailureReason.TokenExchangeFailed, ex.Reason);
    }

    [Fact]
    public async Task ExchangeAsync_TokenOkUsersMeFails_ThrowsUsersMeFailed()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, """{"access_token":"abc","token_type":"Bearer"}"""),
            (HttpStatusCode.Unauthorized, """{"message":"401: Unauthorized"}"""));
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<DiscordOAuthExchangeException>(() =>
            sut.ExchangeAsync("code", CancellationToken.None));

        Assert.Equal(DiscordOAuthFailureReason.UsersMeFailed, ex.Reason);
        Assert.Equal(401, ex.HttpStatusCode);
    }

    [Fact]
    public async Task ExchangeAsync_TokenOkButNoAccessToken_ThrowsTokenExchangeFailed()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, """{"token_type":"Bearer"}"""));
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<DiscordOAuthExchangeException>(() =>
            sut.ExchangeAsync("code", CancellationToken.None));

        Assert.Equal(DiscordOAuthFailureReason.TokenExchangeFailed, ex.Reason);
    }

    [Fact]
    public void Constructor_MissingClientCredentials_Throws()
    {
        var settings = new DiscordSettings
        {
            Provider = DiscordSettings.ProviderDiscord,
            ClientId = "",
            ClientSecret = "",
        };
        var httpClient = new HttpClient(new SequenceHandler());

        Assert.Throws<InvalidOperationException>(() => new DiscordOAuthClient(
            httpClient,
            Options.Create(settings),
            NullLogger<DiscordOAuthClient>.Instance));
    }

    private static DiscordOAuthClient BuildClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new DiscordOAuthClient(
            httpClient,
            Options.Create(Settings()),
            NullLogger<DiscordOAuthClient>.Instance);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();
        private readonly Queue<(HttpStatusCode Status, string Body)> _queue = new();

        public SequenceHandler(params (HttpStatusCode Status, string Body)[] responses)
        {
            foreach (var r in responses) _queue.Enqueue(r);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(
                request.RequestUri!,
                body,
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));

            if (_queue.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            var (status, responseBody) = _queue.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record RecordedRequest(
        Uri Uri,
        string Body,
        string? ContentType,
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
}
