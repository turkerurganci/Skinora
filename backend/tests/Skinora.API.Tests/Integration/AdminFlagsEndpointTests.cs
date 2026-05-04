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
using Skinora.Auth.Configuration;
using Skinora.Fraud.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// Endpoint smoke tests for the AD2 / AD3 / AD4 / AD5 controllers
/// (T54 — 07 §9.2–§9.5). Goal: prove the route mappings, auth gates,
/// JSON envelope shapes and error mappings are wired correctly.
/// Service-layer behaviour is exhaustively covered in
/// <c>FraudFlagServiceTests</c>.
/// </summary>
public class AdminFlagsEndpointTests : IClassFixture<AdminFlagsEndpointTests.Factory>
{
    private const string TestSecret = "admin-flags-test-secret-key-minimum-32-chars!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public AdminFlagsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ---------- AD2 GET /admin/flags ----------

    [Fact]
    public async Task ListFlags_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/flags");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListFlags_NonAdmin_Returns403()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildClient(user.Id, user.SteamId, AuthRoles.User);
        var response = await client.GetAsync("/api/v1/admin/flags");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListFlags_SuperAdmin_ReturnsPagedResponseWithPendingCount()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        await _factory.SeedAccountFlagAsync(seller.Id, ReviewStatus.PENDING);
        await _factory.SeedAccountFlagAsync(seller.Id, ReviewStatus.APPROVED, reviewerAdminId: admin.Id);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.GetAsync("/api/v1/admin/flags");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, data.GetProperty("pendingCount").GetInt32());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
    }

    // ---------- AD3 GET /admin/flags/:id ----------

    [Fact]
    public async Task GetFlag_UnknownId_Returns404FlagNotFound()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.GetAsync($"/api/v1/admin/flags/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("FLAG_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetFlag_Existing_ReturnsDetailEnvelope()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var flagId = await _factory.SeedAccountFlagAsync(seller.Id, ReviewStatus.PENDING);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.GetAsync($"/api/v1/admin/flags/{flagId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(flagId.ToString(), data.GetProperty("id").GetString());
        Assert.Equal("ACCOUNT_LEVEL", data.GetProperty("scope").GetString());
        Assert.Equal("PENDING", data.GetProperty("reviewStatus").GetString());
    }

    // ---------- AD4 POST /admin/flags/:id/approve ----------

    [Fact]
    public async Task ApproveFlag_Unknown_Returns404FlagNotFound()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/flags/{Guid.NewGuid()}/approve",
            new { note = "ok" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("FLAG_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ApproveFlag_AccountLevel_Returns200WithReviewedAt()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var flagId = await _factory.SeedAccountFlagAsync(seller.Id, ReviewStatus.PENDING);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/flags/{flagId}/approve",
            new { note = "Looks fine" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("APPROVED", data.GetProperty("reviewStatus").GetString());
        Assert.True(data.TryGetProperty("transactionStatus", out var txStatus));
        Assert.Equal(JsonValueKind.Null, txStatus.ValueKind);
        Assert.True(data.GetProperty("reviewedAt").GetDateTime() != default);
    }

    [Fact]
    public async Task ApproveFlag_AlreadyReviewed_Returns409AlreadyReviewed()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var flagId = await _factory.SeedAccountFlagAsync(seller.Id,
            status: ReviewStatus.APPROVED, reviewerAdminId: admin.Id);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/flags/{flagId}/approve",
            new { note = "second time" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("ALREADY_REVIEWED",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- AD5 POST /admin/flags/:id/reject ----------

    [Fact]
    public async Task RejectFlag_AccountLevel_Returns200WithReviewedAt()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var flagId = await _factory.SeedAccountFlagAsync(seller.Id, ReviewStatus.PENDING);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/flags/{flagId}/reject",
            new { note = "False positive" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("REJECTED",
            body.GetProperty("data").GetProperty("reviewStatus").GetString());
    }

    // ---------- helpers ----------

    private HttpClient BuildClient(Guid userId, string steamId, string role)
    {
        var token = IssueAccessToken(userId, steamId, role);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string IssueAccessToken(Guid userId, string steamId, string role)
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
                new Claim(AuthClaimTypes.Role, role),
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
        private const string SteamIdPrefix = "76561198555111";

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
                SteamId = $"{SteamIdPrefix}{suffix:D3}",
                SteamDisplayName = $"Tester{suffix:D3}",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
            };
            customize?.Invoke(user);

            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public async Task<Guid> SeedAccountFlagAsync(
            Guid userId,
            ReviewStatus status,
            Guid? reviewerAdminId = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var flag = new FraudFlag
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TransactionId = null,
                Scope = FraudFlagScope.ACCOUNT_LEVEL,
                Type = FraudFlagType.MULTI_ACCOUNT,
                Status = status,
                Details = "{\"matchType\":\"wallet\"}",
                ReviewedAt = status != ReviewStatus.PENDING ? DateTime.UtcNow : null,
                ReviewedByAdminId = status != ReviewStatus.PENDING
                    ? reviewerAdminId ?? throw new InvalidOperationException(
                        "reviewerAdminId required for non-PENDING seed.")
                    : null,
            };
            db.Set<FraudFlag>().Add(flag);
            await db.SaveChangesAsync();
            return flag.Id;
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Set<FraudFlag>().RemoveRange(
                db.Set<FraudFlag>().IgnoreQueryFilters().ToList());
            db.Set<Transaction>().RemoveRange(
                db.Set<Transaction>().IgnoreQueryFilters().ToList());

            // AuditLog is append-only at the AppDbContext layer (06 §4.2);
            // EnforceAppendOnly rejects EntityState.Deleted. Use raw SQL so
            // tests stay isolated without weakening the production guard.
            db.Database.ExecuteSqlRaw("DELETE FROM AuditLogs");

            var seedIds = new[] { Skinora.Shared.Domain.Seed.SeedConstants.SystemUserId };
            db.Set<User>().RemoveRange(
                db.Set<User>().IgnoreQueryFilters().Where(u => !seedIds.Contains(u.Id)));
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
    }
}
