using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
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
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

public class UserProfileEndpointTests : IClassFixture<UserProfileEndpointTests.Factory>
{
    private const string TestSecret = "profile-test-secret-key-minimum-32-chars!!!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string SteamId = "76561198222333555";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public UserProfileEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ---------- /users/me ----------

    [Fact]
    public async Task GetMe_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_Authenticated_ReturnsOwnProfile()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.SteamDisplayName = "PlayerOne";
            u.SteamAvatarUrl = "https://steamcdn.test/avatar.jpg";
            u.CompletedTransactionCount = 24;
            u.SuccessfulTransactionRate = 0.9600m;
            u.DefaultPayoutAddress = "TXyz1234567890abcdef1234567890ab";
            u.DefaultRefundAddress = "TAbcdef1234567890abcdef12345678cd";
            u.MobileAuthenticatorVerified = true;
            u.CreatedAt = DateTime.UtcNow.AddDays(-200);
        });
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(user.Id.ToString(), data.GetProperty("id").GetString());
        Assert.Equal(user.SteamId, data.GetProperty("steamId").GetString());
        Assert.Equal("PlayerOne", data.GetProperty("displayName").GetString());
        Assert.Equal("https://steamcdn.test/avatar.jpg", data.GetProperty("avatarUrl").GetString());
        Assert.Equal("6 ay", data.GetProperty("accountAge").GetString());
        Assert.Equal(24, data.GetProperty("completedTransactionCount").GetInt32());
        Assert.Equal(0.9600m, data.GetProperty("successfulTransactionRate").GetDecimal());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("reputationScore").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("cancelRate").ValueKind);
        Assert.Equal("TXyz1234567890abcdef1234567890ab", data.GetProperty("sellerWalletAddress").GetString());
        Assert.Equal("TAbcdef1234567890abcdef12345678cd", data.GetProperty("refundWalletAddress").GetString());
        Assert.True(data.GetProperty("mobileAuthenticatorActive").GetBoolean());
    }

    // ---------- /users/me/stats ----------

    [Fact]
    public async Task GetMyStats_Authenticated_ReturnsStatsDto()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.CompletedTransactionCount = 24;
            u.SuccessfulTransactionRate = 0.9600m;
        });
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.GetAsync("/api/v1/users/me/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(24, data.GetProperty("completedTransactionCount").GetInt32());
        Assert.Equal(0.9600m, data.GetProperty("successfulTransactionRate").GetDecimal());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("reputationScore").ValueKind);
    }

    // ---------- /users/{steamId} ----------

    [Fact]
    public async Task GetPublic_ExistingUser_ReturnsLimitedProfile()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.SteamDisplayName = "PlayerOne";
            u.SteamAvatarUrl = "https://steamcdn.test/avatar.jpg";
            u.CompletedTransactionCount = 10;
            u.SuccessfulTransactionRate = 0.9000m;
            u.DefaultPayoutAddress = "TShouldNotLeak11111111111111111111";
            u.CreatedAt = DateTime.UtcNow.AddDays(-400);
        });

        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/users/{user.SteamId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(user.SteamId, data.GetProperty("steamId").GetString());
        Assert.Equal("PlayerOne", data.GetProperty("displayName").GetString());
        Assert.Equal("https://steamcdn.test/avatar.jpg", data.GetProperty("avatarUrl").GetString());
        Assert.Equal("1 yıl", data.GetProperty("accountAge").GetString());
        Assert.Equal(10, data.GetProperty("completedTransactionCount").GetInt32());
        Assert.Equal(0.9000m, data.GetProperty("successfulTransactionRate").GetDecimal());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("reputationScore").ValueKind);

        // Wallet + cancelRate must not leak on public profile (07 §5.5).
        Assert.False(data.TryGetProperty("sellerWalletAddress", out _));
        Assert.False(data.TryGetProperty("refundWalletAddress", out _));
        Assert.False(data.TryGetProperty("cancelRate", out _));
    }

    [Fact]
    public async Task GetPublic_MissingUser_Returns404WithUserNotFoundCode()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/users/76561199999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("USER_NOT_FOUND", body.GetProperty("error").GetProperty("code").GetString());
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
        private int _userSuffix;

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public async Task<User> CreateUserAsync(Action<User>? customize = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Interlocked.Increment(ref _userSuffix);
            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = $"{SteamId}{suffix:D2}",
                SteamDisplayName = "Tester",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
            };
            customize?.Invoke(user);
            var desiredCreatedAt = user.CreatedAt;

            db.Set<User>().Add(user);
            await db.SaveChangesAsync();

            // AppDbContext.UpdateAuditFields overwrites CreatedAt to UtcNow on
            // Added state — replay the caller's value with a second save so
            // tests can control AccountAge.
            if (desiredCreatedAt != default && user.CreatedAt != desiredCreatedAt)
            {
                user.CreatedAt = desiredCreatedAt;
                await db.SaveChangesAsync();
            }
            return user;
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seedIds = new[] { Skinora.Shared.Domain.Seed.SeedConstants.SystemUserId };
            db.Set<User>().RemoveRange(db.Set<User>().Where(u => !seedIds.Contains(u.Id)));
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
