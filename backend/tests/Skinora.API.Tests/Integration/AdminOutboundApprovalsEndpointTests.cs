using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Skinora.API.Services.OutboundApprovals;
using Skinora.Auth.Configuration;
using Skinora.Shared.Enums;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T169 doğrulama S4-4 (⚠ GÜVENLİK) — HTTP boundary of the outbound approval
/// endpoints (AD40 / AD41, 07 §9.35): who may call, that the approver is the
/// authenticated admin (never anything in the request), and how each refusal
/// reads. The approval rules themselves run on the real schema in
/// <c>OutboundApprovalTests</c>. Same factory and JWT pattern as
/// <see cref="AdminOutboundClosuresEndpointTests"/> (T157).
/// </summary>
public class AdminOutboundApprovalsEndpointTests : IClassFixture<AdminWalletsEndpointTests.Factory>
{
    private const string TestSecret = "admin-wallets-test-secret-key-minimum-32-chars!!";
    private const string Base = "/api/v1/admin/wallets/outbound-approvals";
    private const string RowId = "11111111-1111-1111-1111-111111111111";

    private readonly AdminWalletsEndpointTests.Factory _factory;

    public AdminOutboundApprovalsEndpointTests(AdminWalletsEndpointTests.Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    public static TheoryData<string, string> Endpoints() => new()
    {
        { "GET", Base },
        { "POST", $"{Base}/{RowId}/approve" },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Anonymous_Is401_AndTheServiceIsNeverReached(string method, string url)
    {
        var response = await Send(_factory.CreateClient(), method, url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_factory.ApprovalFake.Calls);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task AnAdminWithoutManageSettings_Is403_AndTheServiceIsNeverReached(string method, string url)
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, ["VIEW_FLAGS", "VIEW_TRANSACTIONS", "MANAGE_SANCTIONS"]);

        var response = await Send(client, method, url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.ApprovalFake.Calls);
    }

    [Fact]
    public async Task List_AnswersAnAdminWithManageSettings()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, ["MANAGE_SETTINGS"]);

        var response = await client.GetAsync(Base);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("list", Assert.Single(_factory.ApprovalFake.Calls).Call);
    }

    [Fact]
    public async Task Approve_Is200_AndActsAsTheAuthenticatedAdmin_NotAsAnyoneTheBodyNames()
    {
        var admin = await _factory.CreateUserAsync();
        var other = await _factory.CreateUserAsync();
        var rowId = Guid.Parse(RowId);
        _factory.ApprovalFake.Result = ApprovalResult.Ok(new OutboundApprovalDto(
            rowId, BlockchainNetwork.TRON, BlockchainTransactionType.SELLER_PAYOUT,
            97.5m, "TSellerPayout000000000000000000000", DateTime.UtcNow, admin.Id));
        var client = BuildClient(admin.Id, admin.SteamId, ["MANAGE_SETTINGS"]);

        var response = await client.PostAsJsonAsync($"{Base}/{RowId}/approve", new
        {
            approvedByAdminId = other.Id,
            adminId = other.Id,
            amount = 1_000_000m,
            toAddress = "TAttacker00000000000000000000000000",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (call, actor, row) = Assert.Single(_factory.ApprovalFake.Calls);
        Assert.Equal("approve", call);
        Assert.Equal(admin.Id, actor);
        Assert.Equal(rowId, row);
    }

    [Theory]
    [InlineData(ApprovalError.RowNotFound, HttpStatusCode.NotFound, "ROW_NOT_FOUND")]
    [InlineData(ApprovalError.RowNotApprovable, HttpStatusCode.UnprocessableEntity, "ROW_NOT_APPROVABLE")]
    [InlineData(ApprovalError.RowNotOpen, HttpStatusCode.Conflict, "ROW_NOT_OPEN")]
    [InlineData(ApprovalError.AlreadyApproved, HttpStatusCode.Conflict, "ALREADY_APPROVED")]
    public async Task EveryRefusal_MapsToItsStatusAndCode(
        ApprovalError error, HttpStatusCode status, string code)
    {
        var admin = await _factory.CreateUserAsync();
        _factory.ApprovalFake.Result = ApprovalResult.Fail(error, "reason text");
        var client = BuildClient(admin.Id, admin.SteamId, ["MANAGE_SETTINGS"]);

        var response = await client.PostAsJsonAsync($"{Base}/{Guid.NewGuid()}/approve", new { });

        Assert.Equal(status, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(code, json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void EveryApprovalError_HasARowInTheMappingTable()
    {
        // A new refusal without a row here would reach the client as 500 INTERNAL_ERROR.
        var mapped = typeof(AdminOutboundApprovalsEndpointTests)
            .GetMethod(nameof(EveryRefusal_MapsToItsStatusAndCode))!
            .GetCustomAttributes(typeof(Xunit.InlineDataAttribute), false)
            .Cast<Xunit.InlineDataAttribute>()
            .Select(a => (ApprovalError)a.GetData(null!).Single()[0])
            .ToHashSet();

        Assert.Equal(Enum.GetValues<ApprovalError>().ToHashSet(), mapped);
    }

    [Fact]
    public async Task Approve_ANonGuidRowId_IsNotRouted()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, ["MANAGE_SETTINGS"]);

        var response = await client.PostAsJsonAsync($"{Base}/not-a-guid/approve", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_factory.ApprovalFake.Calls);
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
