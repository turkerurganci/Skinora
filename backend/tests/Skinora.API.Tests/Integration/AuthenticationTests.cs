using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Skinora.API.Tests.Common;
using Skinora.Auth.Configuration;
using Skinora.Shared.Persistence;

namespace Skinora.API.Tests.Integration;

public class AuthenticationTests : IClassFixture<HangfireBypassFactory>
{
    private const string TestSecret = "test-jwt-secret-key-minimum-32-characters-long!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string PreviousSecret = "old-jwt-secret-key-minimum-32-characters-long!!!";

    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AuthenticationTests(HangfireBypassFactory factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestSecret);
            builder.UseSetting("Jwt:Issuer", TestIssuer);
            builder.UseSetting("Jwt:Audience", TestAudience);
            builder.UseSetting("Jwt:AccessTokenExpiryMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenExpiryDays", "7");
            builder.UseSetting("Jwt:PreviousSecret", PreviousSecret);

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite("DataSource=:memory:"));
            });
        }).CreateClient();
    }

    #region JWT Validation

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/_diag/auth/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidToken_Returns200()
    {
        var token = GenerateToken(TestSecret, TestIssuer, TestAudience,
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[] { new Claim(AuthClaimTypes.UserId, "user-123") });

        var request = CreateAuthRequest("/api/v1/_diag/auth/protected", token);
        var response = await _client.SendAsync(request);
        var body = await DeserializeResponse(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.Success);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithExpiredToken_Returns401()
    {
        var token = GenerateToken(TestSecret, TestIssuer, TestAudience,
            expires: DateTime.UtcNow.AddMinutes(-5),
            claims: new[] { new Claim(AuthClaimTypes.UserId, "user-123") });

        var request = CreateAuthRequest("/api/v1/_diag/auth/protected", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithInvalidSignature_Returns401()
    {
        var token = GenerateToken("wrong-key-at-least-32-characters-long!!!!", TestIssuer, TestAudience,
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[] { new Claim(AuthClaimTypes.UserId, "user-123") });

        var request = CreateAuthRequest("/api/v1/_diag/auth/protected", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongIssuer_Returns401()
    {
        var token = GenerateToken(TestSecret, "wrong-issuer", TestAudience,
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[] { new Claim(AuthClaimTypes.UserId, "user-123") });

        var request = CreateAuthRequest("/api/v1/_diag/auth/protected", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongAudience_Returns401()
    {
        var token = GenerateToken(TestSecret, TestIssuer, "wrong-audience",
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[] { new Claim(AuthClaimTypes.UserId, "user-123") });

        var request = CreateAuthRequest("/api/v1/_diag/auth/protected", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Key Rotation

    [Fact]
    public async Task ProtectedEndpoint_WithPreviousSecretToken_Returns200()
    {
        var token = GenerateToken(PreviousSecret, TestIssuer, TestAudience,
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[] { new Claim(AuthClaimTypes.UserId, "user-123") });

        var request = CreateAuthRequest("/api/v1/_diag/auth/protected", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region AllowAnonymous

    [Fact]
    public async Task PublicEndpoint_WithoutToken_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/_diag/auth/public");
        var body = await DeserializeResponse(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.Success);
    }

    #endregion

    #region Policy - AdminAccess

    [Fact]
    public async Task AdminEndpoint_WithUserRole_Returns403()
    {
        var token = GenerateToken(TestSecret, TestIssuer, TestAudience,
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[]
            {
                new Claim(AuthClaimTypes.UserId, "user-123"),
                new Claim(AuthClaimTypes.Role, AuthRoles.User),
            });

        var request = CreateAuthRequest("/api/v1/_diag/auth/admin", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoint_WithAdminRole_Returns200()
    {
        var token = GenerateToken(TestSecret, TestIssuer, TestAudience,
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[]
            {
                new Claim(AuthClaimTypes.UserId, "admin-1"),
                new Claim(AuthClaimTypes.Role, AuthRoles.Admin),
            });

        var request = CreateAuthRequest("/api/v1/_diag/auth/admin", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoint_WithSuperAdminRole_Returns200()
    {
        var token = GenerateToken(TestSecret, TestIssuer, TestAudience,
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[]
            {
                new Claim(AuthClaimTypes.UserId, "superadmin-1"),
                new Claim(AuthClaimTypes.Role, AuthRoles.SuperAdmin),
            });

        var request = CreateAuthRequest("/api/v1/_diag/auth/admin", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Policy - SuperAdmin

    [Fact]
    public async Task SuperAdminEndpoint_WithAdminRole_Returns403()
    {
        var token = GenerateToken(TestSecret, TestIssuer, TestAudience,
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[]
            {
                new Claim(AuthClaimTypes.UserId, "admin-1"),
                new Claim(AuthClaimTypes.Role, AuthRoles.Admin),
            });

        var request = CreateAuthRequest("/api/v1/_diag/auth/super-admin", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminEndpoint_WithSuperAdminRole_Returns200()
    {
        var token = GenerateToken(TestSecret, TestIssuer, TestAudience,
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[]
            {
                new Claim(AuthClaimTypes.UserId, "superadmin-1"),
                new Claim(AuthClaimTypes.Role, AuthRoles.SuperAdmin),
            });

        var request = CreateAuthRequest("/api/v1/_diag/auth/super-admin", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Policy - Permission

    [Fact]
    public async Task PermissionEndpoint_WithoutPermission_Returns403()
    {
        var token = GenerateToken(TestSecret, TestIssuer, TestAudience,
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[]
            {
                new Claim(AuthClaimTypes.UserId, "admin-1"),
                new Claim(AuthClaimTypes.Role, AuthRoles.Admin),
            });

        var request = CreateAuthRequest("/api/v1/_diag/auth/permission", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PermissionEndpoint_WithMatchingPermission_Returns200()
    {
        var token = GenerateToken(TestSecret, TestIssuer, TestAudience,
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[]
            {
                new Claim(AuthClaimTypes.UserId, "admin-1"),
                new Claim(AuthClaimTypes.Role, AuthRoles.Admin),
                new Claim(AuthClaimTypes.Permission, "ManageUsers"),
            });

        var request = CreateAuthRequest("/api/v1/_diag/auth/permission", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PermissionEndpoint_SuperAdminBypassesPermission_Returns200()
    {
        var token = GenerateToken(TestSecret, TestIssuer, TestAudience,
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[]
            {
                new Claim(AuthClaimTypes.UserId, "superadmin-1"),
                new Claim(AuthClaimTypes.Role, AuthRoles.SuperAdmin),
            });

        var request = CreateAuthRequest("/api/v1/_diag/auth/permission", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Claims Extraction

    [Fact]
    public async Task ProtectedEndpoint_ExtractsSubClaim()
    {
        var token = GenerateToken(TestSecret, TestIssuer, TestAudience,
            expires: DateTime.UtcNow.AddMinutes(15),
            claims: new[] { new Claim(AuthClaimTypes.UserId, "user-456") });

        var request = CreateAuthRequest("/api/v1/_diag/auth/protected", token);
        var response = await _client.SendAsync(request);
        var body = await DeserializeResponse(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user-456", body.Data?.GetProperty("sub").GetString());
    }

    #endregion

    #region Helpers

    private static string GenerateToken(
        string secret, string issuer, string audience,
        DateTime expires, Claim[] claims, DateTime? notBefore = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = notBefore ?? (expires < DateTime.UtcNow ? expires.AddMinutes(-15) : null),
            Expires = expires,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = credentials,
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }

    private static HttpRequestMessage CreateAuthRequest(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<TestApiResponse> DeserializeResponse(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TestApiResponse>(content, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize response: {content}");
    }

    private record TestApiResponse(
        bool Success,
        JsonElement? Data,
        TestApiError? Error,
        string? TraceId);

    private record TestApiError(
        string Code,
        string Message,
        JsonElement? Details);

    #endregion
}
