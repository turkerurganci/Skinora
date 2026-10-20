using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Skinora.API.Services.WalletClosures;
using Skinora.Auth.Configuration;
using Skinora.Shared.Enums;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T157 — HTTP boundary of the two-person closure tool (AD33–AD37) and of the
/// wallet overview (AD38): who may call, that the acting admin is the
/// authenticated one (never a body field), and how each refusal reads. The
/// rules themselves are tested on the real schema in
/// <c>OutboundRowClosureTests</c>.
/// </summary>
public class AdminOutboundClosuresEndpointTests : IClassFixture<AdminWalletsEndpointTests.Factory>
{
    private const string TestSecret = "admin-wallets-test-secret-key-minimum-32-chars!!";
    private const string Base = "/api/v1/admin/wallets";
    private const string Hash = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly AdminWalletsEndpointTests.Factory _factory;

    public AdminOutboundClosuresEndpointTests(AdminWalletsEndpointTests.Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    public static TheoryData<string, string> Endpoints() => new()
    {
        { "GET", Base },
        { "GET", Base + "/outbound-closures" },
        { "GET", Base + "/outbound-closures/candidates" },
        { "POST", Base + "/outbound-closures" },
        { "POST", Base + "/outbound-closures/" + "11111111-1111-1111-1111-111111111111/approve" },
        { "POST", Base + "/outbound-closures/" + "11111111-1111-1111-1111-111111111111/reject" },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Anonymous_Is401(string method, string url)
    {
        var response = await Send(_factory.CreateClient(), method, url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task AnAdminWithoutManageSettings_Is403_AndTheServiceIsNeverReached(string method, string url)
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, ["VIEW_FLAGS", "VIEW_TRANSACTIONS", "MANAGE_SANCTIONS"]);

        var response = await Send(client, method, url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.ClosureFake.Calls);
    }

    [Fact]
    public async Task Propose_ActsAsTheAuthenticatedAdmin_NotAsAnyoneTheBodyNames()
    {
        var admin = await _factory.CreateUserAsync();
        var other = await _factory.CreateUserAsync();
        _factory.ClosureFake.Result = ClosureResult.Fail(ClosureError.Validation, "stop here");
        var client = BuildClient(admin.Id, admin.SteamId, ["MANAGE_SETTINGS"]);
        var rowId = Guid.NewGuid();

        await client.PostAsJsonAsync(Base + "/outbound-closures", new
        {
            blockchainTransactionId = rowId,
            txHash = Hash,
            reason = "because",
            proposedByAdminId = other.Id,
            adminId = other.Id,
        });

        var (call, actor, args) = Assert.Single(_factory.ClosureFake.Calls);
        Assert.Equal("propose", call);
        Assert.Equal(admin.Id, actor);
        Assert.Equal(rowId, args[0]);
        Assert.Equal(Hash, args[1]);
    }

    [Fact]
    public async Task Approve_ActsAsTheAuthenticatedAdmin_AndForwardsTheRetypedHash()
    {
        var admin = await _factory.CreateUserAsync();
        _factory.ClosureFake.Result = ClosureResult.Fail(ClosureError.Validation, "stop here");
        var client = BuildClient(admin.Id, admin.SteamId, ["MANAGE_SETTINGS"]);
        var closureId = Guid.NewGuid();

        await client.PostAsJsonAsync($"{Base}/outbound-closures/{closureId}/approve", new { txHash = Hash });

        var (call, actor, args) = Assert.Single(_factory.ClosureFake.Calls);
        Assert.Equal("approve", call);
        Assert.Equal(admin.Id, actor);
        Assert.Equal(closureId, args[0]);
        Assert.Equal(Hash, args[1]);
    }

    [Theory]
    [InlineData(ClosureError.Validation, HttpStatusCode.BadRequest, "VALIDATION_ERROR")]
    [InlineData(ClosureError.InvalidHash, HttpStatusCode.BadRequest, "INVALID_TX_HASH")]
    [InlineData(ClosureError.HashMismatch, HttpStatusCode.BadRequest, "HASH_CONFIRMATION_MISMATCH")]
    [InlineData(ClosureError.SelfApproval, HttpStatusCode.Forbidden, "SELF_APPROVAL_FORBIDDEN")]
    [InlineData(ClosureError.RowNotFound, HttpStatusCode.NotFound, "ROW_NOT_FOUND")]
    [InlineData(ClosureError.ClosureNotFound, HttpStatusCode.NotFound, "CLOSURE_NOT_FOUND")]
    [InlineData(ClosureError.RowNotEthereum, HttpStatusCode.UnprocessableEntity, "ROW_NOT_ETHEREUM")]
    [InlineData(ClosureError.RowNotOutbound, HttpStatusCode.UnprocessableEntity, "ROW_NOT_OUTBOUND")]
    [InlineData(ClosureError.HashNotSuccessful, HttpStatusCode.UnprocessableEntity, "HASH_NOT_SUCCESSFUL")]
    [InlineData(ClosureError.HashNotFound, HttpStatusCode.UnprocessableEntity, "HASH_NOT_FOUND")]
    [InlineData(ClosureError.HashDoesNotMatchRow, HttpStatusCode.UnprocessableEntity, "HASH_DOES_NOT_MATCH_ROW")]
    [InlineData(ClosureError.RowTransferUnresolved, HttpStatusCode.UnprocessableEntity, "ROW_TRANSFER_UNRESOLVED")]
    [InlineData(ClosureError.AmbiguousRowMatch, HttpStatusCode.Conflict, "AMBIGUOUS_ROW_MATCH")]
    [InlineData(ClosureError.RowNotOpen, HttpStatusCode.Conflict, "ROW_NOT_OPEN")]
    [InlineData(ClosureError.ClosureNotOpen, HttpStatusCode.Conflict, "CLOSURE_NOT_OPEN")]
    [InlineData(ClosureError.HashAlreadyBound, HttpStatusCode.Conflict, "HASH_ALREADY_BOUND")]
    [InlineData(ClosureError.OpenClosureExists, HttpStatusCode.Conflict, "CLOSURE_ALREADY_PROPOSED")]
    [InlineData(ClosureError.HashNotFinal, HttpStatusCode.Conflict, "HASH_NOT_FINAL")]
    [InlineData(ClosureError.Conflict, HttpStatusCode.Conflict, "CLOSURE_CONFLICT")]
    [InlineData(ClosureError.ChainUnavailable, HttpStatusCode.BadGateway, "CHAIN_SERVICE_UNAVAILABLE")]
    public async Task EveryRefusal_MapsToItsStatusAndCode(
        ClosureError error, HttpStatusCode status, string code)
    {
        var admin = await _factory.CreateUserAsync();
        _factory.ClosureFake.Result = ClosureResult.Fail(error, "reason text");
        var client = BuildClient(admin.Id, admin.SteamId, ["MANAGE_SETTINGS"]);

        var response = await client.PostAsJsonAsync(
            $"{Base}/outbound-closures/{Guid.NewGuid()}/approve", new { txHash = Hash });

        Assert.Equal(status, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(code, json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void EveryClosureError_HasARowInTheMappingTable()
    {
        // A new refusal without a row here would reach the client as 500 INTERNAL_ERROR.
        var mapped = typeof(AdminOutboundClosuresEndpointTests)
            .GetMethod(nameof(EveryRefusal_MapsToItsStatusAndCode))!
            .GetCustomAttributes(typeof(Xunit.InlineDataAttribute), false)
            .Cast<Xunit.InlineDataAttribute>()
            .Select(a => (ClosureError)a.GetData(null!).Single()[0])
            .ToHashSet();

        Assert.Equal(Enum.GetValues<ClosureError>().ToHashSet(), mapped);
    }

    [Fact]
    public async Task OverviewAndLists_AnswerAnAdminWithManageSettings()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, ["MANAGE_SETTINGS"]);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Base)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Base + "/outbound-closures")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Base + "/outbound-closures/candidates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Base + "/outbound-closures?status=PROPOSED")).StatusCode);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string method, string url) =>
        method == "GET"
            ? client.GetAsync(url)
            : client.PostAsJsonAsync(url, new { });

    private HttpClient BuildClient(Guid userId, string steamId, IReadOnlyList<string> permissions)
    {
        var handler = new JwtSecurityTokenHandler();
        var claims = new List<Claim>
        {
            new(AuthClaimTypes.UserId, userId.ToString()),
            new(AuthClaimTypes.SteamId, steamId),
            new(AuthClaimTypes.Role, AuthRoles.Admin),
        };
        claims.AddRange(permissions.Select(p => new Claim(AuthClaimTypes.Permission, p)));
        var token = handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "skinora",
            Audience = "skinora-client",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)), SecurityAlgorithms.HmacSha256),
        }));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
