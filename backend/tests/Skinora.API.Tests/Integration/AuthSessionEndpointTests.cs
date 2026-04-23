using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Medallion.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Startup;
using Skinora.API.Tests.Common;
using Skinora.Auth.Application.Session;
using Skinora.Auth.Configuration;
using Skinora.Auth.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

public class AuthSessionEndpointTests : IClassFixture<AuthSessionEndpointTests.Factory>
{
    private const string TestSecret = "session-test-secret-key-minimum-32-chars!!!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string SteamId = "76561198222333444";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public AuthSessionEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ---------- /auth/me ----------

    [Fact]
    public async Task GetMe_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_Authenticated_ReturnsProfileDto()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = true;
            u.TosAcceptedAt = DateTime.UtcNow.AddDays(-1);
            u.PreferredLanguage = "tr";
            u.DefaultPayoutAddress = "TRC20xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";
            u.SteamAvatarUrl = "https://steamcdn/avatar.jpg";
        });
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(user.Id.ToString(), data.GetProperty("id").GetString());
        Assert.Equal(user.SteamId, data.GetProperty("steamId").GetString());
        Assert.Equal(user.SteamDisplayName, data.GetProperty("displayName").GetString());
        Assert.Equal("https://steamcdn/avatar.jpg", data.GetProperty("avatarUrl").GetString());
        Assert.True(data.GetProperty("mobileAuthenticatorActive").GetBoolean());
        Assert.True(data.GetProperty("tosAccepted").GetBoolean());
        Assert.Equal("user", data.GetProperty("role").GetString());
        Assert.Equal("tr", data.GetProperty("language").GetString());
        Assert.True(data.GetProperty("hasSellerWallet").GetBoolean());
        Assert.False(data.GetProperty("hasRefundWallet").GetBoolean());
    }

    // ---------- /auth/refresh ----------

    [Fact]
    public async Task Refresh_NoCookie_Returns401WithMissingCode()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("REFRESH_TOKEN_MISSING", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Refresh_ValidCookie_RotatesAndReturnsAccessToken()
    {
        var user = await _factory.CreateUserAsync();
        var (plainText, _) = await _factory.SeedRefreshTokenAsync(user.Id);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"refreshToken={plainText}");

        var response = await client.PostAsync("/api/v1/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("accessToken").GetString()));
        Assert.True(data.GetProperty("expiresIn").GetInt32() > 0);

        // New refresh cookie set with fresh plaintext.
        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        var newRefreshCookie = setCookies.FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(newRefreshCookie);
        Assert.Contains("httponly", newRefreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", newRefreshCookie, StringComparison.OrdinalIgnoreCase);
        var newPlain = newRefreshCookie!.Split(';')[0]["refreshToken=".Length..];
        Assert.NotEqual(plainText, newPlain);

        // Old token is revoked + successor pointer set; new token is active.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = await db.Set<RefreshToken>()
            .Where(t => t.UserId == user.Id)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.True(tokens[0].IsRevoked);
        Assert.NotNull(tokens[0].ReplacedByTokenId);
        Assert.Equal(tokens[1].Id, tokens[0].ReplacedByTokenId);
        Assert.False(tokens[1].IsRevoked);
    }

    [Fact]
    public async Task Refresh_WithRotatedCookie_ReturnsInvalidAndMassRevokes()
    {
        var user = await _factory.CreateUserAsync();
        var (plainText, _) = await _factory.SeedRefreshTokenAsync(user.Id);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"refreshToken={plainText}");

        // First rotation — legitimate.
        var first = await client.PostAsync("/api/v1/auth/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Replay the original rotated token — compromise signal.
        var replay = await client.PostAsync("/api/v1/auth/refresh", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        var body = await replay.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("REFRESH_TOKEN_INVALID", body.GetProperty("error").GetProperty("code").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var activeCount = await db.Set<RefreshToken>()
            .CountAsync(t => t.UserId == user.Id && !t.IsRevoked);
        Assert.Equal(0, activeCount);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Returns401ExpiredAndClearsCookie()
    {
        var user = await _factory.CreateUserAsync();
        var (plainText, _) = await _factory.SeedRefreshTokenAsync(
            user.Id, expiresAt: DateTime.UtcNow.AddMinutes(-5));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"refreshToken={plainText}");

        var response = await client.PostAsync("/api/v1/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("REFRESH_TOKEN_EXPIRED", body.GetProperty("error").GetProperty("code").GetString());

        var cleared = response.Headers.TryGetValues("Set-Cookie", out var values)
            && values.Any(v => v.StartsWith("refreshToken=")
                && v.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.True(cleared);
    }

    // ---------- /auth/logout ----------

    [Fact]
    public async Task Logout_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_Authenticated_RevokesTokenAndClearsCookie()
    {
        var user = await _factory.CreateUserAsync();
        var (plainText, tokenId) = await _factory.SeedRefreshTokenAsync(user.Id);

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add("Cookie", $"refreshToken={plainText}");

        var response = await client.PostAsync("/api/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Cookie cleared.
        var cleared = response.Headers.GetValues("Set-Cookie")
            .Any(v => v.StartsWith("refreshToken=")
                && v.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.True(cleared);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = await db.Set<RefreshToken>().SingleAsync(t => t.Id == tokenId);
        Assert.True(token.IsRevoked);
        Assert.NotNull(token.RevokedAt);
    }

    [Fact]
    public async Task Logout_Authenticated_NoCookie_IsIdempotent()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PostAsync("/api/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- helpers ----------

    private HttpClient BuildAuthenticatedClient(Guid userId, string steamId)
    {
        var token = IssueAccessToken(userId, steamId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string IssueAccessToken(Guid userId, string steamId)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = TestIssuer,
            Audience = TestAudience,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(AuthClaimTypes.UserId, userId.ToString()),
                new Claim(AuthClaimTypes.SteamId, steamId),
                new Claim(AuthClaimTypes.Role, AuthRoles.User),
            }),
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = creds,
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private sealed class NoopBackgroundJobScheduler : IBackgroundJobScheduler
    {
        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
            => Guid.NewGuid().ToString("N");
        public string Enqueue<T>(Expression<Action<T>> methodCall)
            => Guid.NewGuid().ToString("N");
        public bool Delete(string jobId) => true;
        public void AddOrUpdateRecurring<T>(
            string jobId, Expression<Action<T>> methodCall, string cronExpression)
        { }
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public async Task<User> CreateUserAsync(Action<User>? customize = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = SteamId,
                SteamDisplayName = "Tester",
                PreferredLanguage = "en",
            };
            customize?.Invoke(user);
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public async Task<(string plainText, Guid tokenId)> SeedRefreshTokenAsync(
            Guid userId, DateTime? expiresAt = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain)));
            var token = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = hash,
                ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
                IsRevoked = false,
            };
            db.Set<RefreshToken>().Add(token);
            await db.SaveChangesAsync();
            return (plain, token.Id);
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<RefreshToken>().RemoveRange(db.Set<RefreshToken>());
            db.Set<User>().RemoveRange(db.Set<User>());
            db.SaveChanges();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(local);Database=SkinoraTest;Integrated Security=true;TrustServerCertificate=true");
            builder.UseSetting("Hangfire:DashboardEnabled", "false");

            builder.UseSetting("Jwt:Secret", TestSecret);
            builder.UseSetting("Jwt:Issuer", TestIssuer);
            builder.UseSetting("Jwt:Audience", TestAudience);
            builder.UseSetting("Jwt:AccessTokenExpiryMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenExpiryDays", "7");
            builder.UseSetting("Jwt:PreviousSecret", "");

            builder.UseSetting("SteamOpenId:Realm", "https://skinora.test");
            builder.UseSetting("SteamOpenId:ReturnToUrl",
                "https://skinora.test/api/v1/auth/steam/callback");
            builder.UseSetting("SteamOpenId:FrontendCallbackUrl",
                "https://localhost:3000/auth/callback");
            builder.UseSetting("SteamOpenId:DefaultReturnPath", "/dashboard");
            builder.UseSetting("SteamOpenId:WebApiKey", "");

            builder.ConfigureServices(services =>
            {
                // EF Core — strip SqlServer stack and re-add SQLite.
                var efDescriptors = services
                    .Where(d =>
                        d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                        d.ServiceType == typeof(DbContextOptions) ||
                        d.ServiceType == typeof(AppDbContext) ||
                        (d.ServiceType.IsGenericType &&
                         d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")) ||
                        (d.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore",
                            StringComparison.Ordinal) ?? false))
                    .ToList();
                foreach (var d in efDescriptors) services.Remove(d);

                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

                var hangfireDescriptors = services
                    .Where(d =>
                        (d.ServiceType.Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) ?? false) ||
                        (d.ImplementationType?.Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) ?? false) ||
                        (d.ImplementationFactory?.Method.DeclaringType?.Assembly.GetName().Name?
                            .StartsWith("Hangfire", StringComparison.Ordinal) ?? false))
                    .ToList();
                foreach (var d in hangfireDescriptors) services.Remove(d);

                var startupHookDescriptors = services
                    .Where(d =>
                        d.ImplementationType == typeof(OutboxStartupHook) ||
                        d.ImplementationType == typeof(SettingsBootstrapHook))
                    .ToList();
                foreach (var d in startupHookDescriptors) services.Remove(d);

                services.RemoveAll<IBackgroundJobScheduler>();
                services.AddSingleton<IBackgroundJobScheduler, NoopBackgroundJobScheduler>();

                services.RemoveAll<IDistributedLockProvider>();
                services.AddSingleton<IDistributedLockProvider, InMemoryDistributedLockProvider>();

                var healthCheckDescriptors = services
                    .Where(d => d.ServiceType.FullName?.Contains("HealthCheck",
                        StringComparison.Ordinal) == true)
                    .ToList();
                foreach (var d in healthCheckDescriptors) services.Remove(d);
                services.AddHealthChecks();

                services.RemoveAll<IRateLimiterStore>();
                services.AddSingleton<IRateLimiterStore, InMemoryRateLimiterStore>();

                // Null refresh-token cache — tests don't spin up Redis.
                services.RemoveAll<IRefreshTokenCache>();
                services.AddSingleton<IRefreshTokenCache, NullRefreshTokenCache>();
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            return host;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }
}
