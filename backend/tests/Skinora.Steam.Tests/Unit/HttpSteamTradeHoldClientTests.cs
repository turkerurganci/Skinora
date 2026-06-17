using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Shared.Steam;
using Skinora.Steam.Application.Inventory;

namespace Skinora.Steam.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="HttpSteamTradeHoldClient"/> (WP6). A stub
/// <see cref="HttpMessageHandler"/> covers the request shape (URI, method,
/// X-Internal-Key header), the active/inactive mapping, and the fail-closed
/// behaviour for every non-success outcome (5xx / transport / malformed body).
/// </summary>
public sealed class HttpSteamTradeHoldClientTests
{
    private const string SteamId = "76561198000000001";
    private const string Token = "abc123xyz";
    private const string InternalKey = "test-internal-key";

    [Fact]
    public async Task ProbeAsync_Returns_Active_When_Sidecar_Reports_Active()
    {
        var handler = new StubHandler(_ => OkJson(@"{""active"":true,""escrowEndDurationSeconds"":0}"));
        var sut = BuildClient(handler);

        var result = await sut.ProbeAsync(SteamId, Token, CancellationToken.None);

        Assert.True(result.Available);
        Assert.True(result.MobileAuthenticatorActive);
    }

    [Fact]
    public async Task ProbeAsync_Returns_Inactive_When_Sidecar_Reports_Hold()
    {
        var handler = new StubHandler(_ =>
            OkJson(@"{""active"":false,""escrowEndDurationSeconds"":1296000}"));
        var sut = BuildClient(handler);

        var result = await sut.ProbeAsync(SteamId, Token, CancellationToken.None);

        Assert.True(result.Available);
        Assert.False(result.MobileAuthenticatorActive);
    }

    [Fact]
    public async Task ProbeAsync_Sends_Get_With_InternalKey_And_AccessToken_Query()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req =>
        {
            captured = req;
            return OkJson(@"{""active"":true,""escrowEndDurationSeconds"":0}");
        });
        var sut = BuildClient(handler);

        await sut.ProbeAsync(SteamId, Token, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Contains($"/api/trade-hold/{SteamId}", captured.RequestUri!.AbsoluteUri);
        Assert.Contains($"accessToken={Token}", captured.RequestUri!.AbsoluteUri);
        Assert.True(captured.Headers.TryGetValues("X-Internal-Key", out var keyValues));
        Assert.Equal(InternalKey, keyValues!.Single());
    }

    [Fact]
    public async Task ProbeAsync_Returns_Unavailable_On_503()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var sut = BuildClient(handler);

        var result = await sut.ProbeAsync(SteamId, Token, CancellationToken.None);

        Assert.False(result.Available);
        Assert.False(result.MobileAuthenticatorActive);
    }

    [Fact]
    public async Task ProbeAsync_Returns_Unavailable_On_TransportError()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var sut = BuildClient(handler);

        var result = await sut.ProbeAsync(SteamId, Token, CancellationToken.None);

        Assert.False(result.Available);
    }

    [Fact]
    public async Task ProbeAsync_Returns_Unavailable_On_Malformed_Body()
    {
        var handler = new StubHandler(_ => OkJson("not-json"));
        var sut = BuildClient(handler);

        var result = await sut.ProbeAsync(SteamId, Token, CancellationToken.None);

        Assert.False(result.Available);
    }

    [Theory]
    [InlineData("", Token)]
    [InlineData(SteamId, "")]
    public async Task ProbeAsync_Returns_Unavailable_Without_RoundTrip_On_Empty_Inputs(
        string steamId, string token)
    {
        var called = false;
        var handler = new StubHandler(_ =>
        {
            called = true;
            return OkJson(@"{""active"":true,""escrowEndDurationSeconds"":0}");
        });
        var sut = BuildClient(handler);

        var result = await sut.ProbeAsync(steamId, token, CancellationToken.None);

        Assert.False(result.Available);
        Assert.False(called);
    }

    // ---------- helpers ----------

    private static HttpResponseMessage OkJson(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpSteamTradeHoldClient BuildClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://sidecar.test/"),
        };
        var options = Options.Create(new SteamSidecarOptions
        {
            BaseUrl = "http://sidecar.test",
            InternalKey = InternalKey,
            TimeoutSeconds = 30,
        });
        return new HttpSteamTradeHoldClient(
            http, options, NullLogger<HttpSteamTradeHoldClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
