using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T166 — ToS + 18+ acceptance is enforced on the server
/// (<c>TosAcceptanceGateFilter</c>). The client can skip the modal; the API
/// must still refuse every gated endpoint until the account has accepted.
/// </summary>
/// <remarks>
/// Shares <see cref="TosAcceptEndpointTests.Factory"/> (SQLite, Development
/// environment, in-memory rate limiter) so the acceptance endpoint and the
/// gated endpoints run in one host and the full "rejected → accept → allowed"
/// path is exercised end to end.
/// </remarks>
public class TosAcceptanceGateTests : IClassFixture<TosAcceptEndpointTests.Factory>
{
    private const string GatedRealEndpoint = "/api/v1/users/me/settings";
    private const string GatedProbe = "/api/v1/_diag/auth/protected";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly TosAcceptEndpointTests.Factory _factory;

    public TosAcceptanceGateTests(TosAcceptEndpointTests.Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Theory]
    [InlineData(GatedRealEndpoint)]
    [InlineData(GatedProbe)]
    public async Task NotAccepted_GatedEndpoint_Returns403TosNotAccepted(string path)
    {
        var userId = await _factory.CreateUserAsync();
        var client = BuildClient(userId);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("TOS_NOT_ACCEPTED", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task TosStampedButAgeMissing_GatedEndpoint_Returns403()
    {
        // The rule is BOTH stamps — a pre-T30 acceptance without the 18+
        // attestation does not pass.
        var userId = await _factory.CreateUserAsync(alreadyAccepted: true, ageConfirmed: false);
        var client = BuildClient(userId);

        var response = await client.GetAsync(GatedProbe);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("TOS_NOT_ACCEPTED", await ReadErrorCodeAsync(response));
    }

    [Theory]
    [InlineData(GatedRealEndpoint)]
    [InlineData(GatedProbe)]
    public async Task Accepted_GatedEndpoint_Returns200(string path)
    {
        var userId = await _factory.CreateUserAsync(alreadyAccepted: true);
        var client = BuildClient(userId);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NotAccepted_AcceptThenRetry_IsAllowed()
    {
        var userId = await _factory.CreateUserAsync();
        var client = BuildClient(userId);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(GatedRealEndpoint)).StatusCode);

        var accept = await client.PostAsJsonAsync(
            "/api/v1/auth/tos/accept",
            new { tosVersion = "1.0", ageOver18 = true });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(GatedRealEndpoint)).StatusCode);
    }

    [Fact]
    public async Task NotAccepted_SessionSurfaceStaysReachable()
    {
        // /auth/me must answer so the client can see tosAccepted = false and
        // show the modal; logout must work so the user can leave.
        var userId = await _factory.CreateUserAsync();
        var client = BuildClient(userId);

        var me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await me.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.False(body.GetProperty("data").GetProperty("tosAccepted").GetBoolean());

        var logout = await client.PostAsync("/api/v1/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
    }

    [Fact]
    public async Task AgeMissing_MeReportsNotAccepted()
    {
        // /auth/me and the gate read the same rule: an account the gate
        // rejects must be told to show the modal.
        var userId = await _factory.CreateUserAsync(alreadyAccepted: true, ageConfirmed: false);
        var client = BuildClient(userId);

        var me = await client.GetAsync("/api/v1/auth/me");
        var body = await me.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        Assert.False(body.GetProperty("data").GetProperty("tosAccepted").GetBoolean());
    }

    [Fact]
    public async Task NotAccepted_AnonymousEndpoint_NotGated()
    {
        // The gate guards authorized endpoints only. An anonymous endpoint is
        // reachable without a token anyway; sending one must not change that.
        var userId = await _factory.CreateUserAsync();
        var client = BuildClient(userId);

        var response = await client.GetAsync("/api/v1/_diag/auth/public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient BuildClient(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TosAcceptEndpointTests.IssueAccessToken(userId));
        return client;
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("error").GetProperty("code").GetString();
    }
}
