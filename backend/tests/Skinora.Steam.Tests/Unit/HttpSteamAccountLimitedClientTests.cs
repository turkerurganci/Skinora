using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Shared.Steam;
using Skinora.Steam.Application.Inventory;

namespace Skinora.Steam.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="HttpSteamAccountLimitedClient"/> (08 §2.2a).
///
/// <para>
/// The case that matters most here is the one a hand-written envelope gets
/// wrong by default: a 200 response WITHOUT the <c>limited</c> field. Bound to
/// a plain <c>bool</c> it would read as <c>false</c> — "not limited" — and the
/// gate this client feeds would open on a body nobody understood. That is the
/// same fail-open the sidecar's own parser refuses one layer down, and the
/// reason the field is deserialized as <c>bool?</c>.
/// </para>
/// </summary>
public sealed class HttpSteamAccountLimitedClientTests
{
    private const string SteamId = "76561198000000001";
    private const string InternalKey = "test-internal-key";

    [Fact]
    public async Task ProbeAsync_Returns_Limited_When_Sidecar_Reports_Limited()
    {
        var handler = new StubHandler(_ =>
            OkJson(@"{""limited"":true,""samples"":1,""source"":""live""}"));

        var result = await BuildClient(handler).ProbeAsync(SteamId, CancellationToken.None);

        Assert.True(result.Available);
        Assert.True(result.IsLimited);
    }

    [Fact]
    public async Task ProbeAsync_Returns_Clear_When_Sidecar_Reports_Not_Limited()
    {
        var handler = new StubHandler(_ =>
            OkJson(@"{""limited"":false,""samples"":3,""source"":""live""}"));

        var result = await BuildClient(handler).ProbeAsync(SteamId, CancellationToken.None);

        Assert.True(result.Available);
        Assert.False(result.IsLimited);
    }

    [Fact]
    public async Task ProbeAsync_Returns_Unavailable_When_Body_Omits_The_Limited_Field()
    {
        // The fail-open this client exists to refuse: a 200 whose body it does
        // not understand must never be read as "this account may trade".
        var handler = new StubHandler(_ => OkJson(@"{""samples"":3,""source"":""live""}"));

        var result = await BuildClient(handler).ProbeAsync(SteamId, CancellationToken.None);

        Assert.False(result.Available);
        Assert.False(result.IsLimited);
    }

    [Fact]
    public async Task ProbeAsync_Sends_Get_With_InternalKey()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req =>
        {
            captured = req;
            return OkJson(@"{""limited"":false,""samples"":3,""source"":""live""}");
        });

        await BuildClient(handler).ProbeAsync(SteamId, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Contains($"/api/account-limited/{SteamId}", captured.RequestUri!.AbsoluteUri);
        // No accessToken: the profile document is anonymous, which is what puts
        // this check within reach of the seller-side gate (08 §2.2a).
        Assert.DoesNotContain("accessToken", captured.RequestUri!.AbsoluteUri);
        Assert.True(captured.Headers.TryGetValues("X-Internal-Key", out var keyValues));
        Assert.Equal(InternalKey, keyValues!.Single());
    }

    [Fact]
    public async Task ProbeAsync_Returns_Unavailable_On_503()
    {
        // The sidecar's own fail-closed branch (STEAM_PROFILE_UNREADABLE).
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await BuildClient(handler).ProbeAsync(SteamId, CancellationToken.None);

        Assert.False(result.Available);
    }

    [Fact]
    public async Task ProbeAsync_Returns_Unavailable_On_TransportError()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var result = await BuildClient(handler).ProbeAsync(SteamId, CancellationToken.None);

        Assert.False(result.Available);
    }

    [Fact]
    public async Task ProbeAsync_Returns_Unavailable_On_Malformed_Body()
    {
        var handler = new StubHandler(_ => OkJson("not-json"));

        var result = await BuildClient(handler).ProbeAsync(SteamId, CancellationToken.None);

        Assert.False(result.Available);
    }

    [Fact]
    public async Task ProbeAsync_Returns_Unavailable_Without_RoundTrip_On_Empty_SteamId()
    {
        var called = false;
        var handler = new StubHandler(_ =>
        {
            called = true;
            return OkJson(@"{""limited"":false,""samples"":3,""source"":""live""}");
        });

        var result = await BuildClient(handler).ProbeAsync("", CancellationToken.None);

        Assert.False(result.Available);
        Assert.False(called);
    }

    // ---------- helpers ----------

    private static HttpResponseMessage OkJson(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpSteamAccountLimitedClient BuildClient(HttpMessageHandler handler)
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
        return new HttpSteamAccountLimitedClient(
            http, options, NullLogger<HttpSteamAccountLimitedClient>.Instance);
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
