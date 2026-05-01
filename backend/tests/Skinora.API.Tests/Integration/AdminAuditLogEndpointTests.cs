using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
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
using Skinora.Admin.Domain.Entities;
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Startup;
using Skinora.API.Tests.Common;
using Skinora.Auth.Configuration;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T42 — End-to-end coverage for <c>GET /api/v1/admin/audit-logs</c>
/// (07 §9.19, AD18). Mirrors the AdminSettingsEndpointTests SQLite + JWT
/// fixture so the VIEW_AUDIT_LOG permission gate, controller wiring, query
/// service and DTO shape all exercise the real DI graph.
/// </summary>
public class AdminAuditLogEndpointTests
    : IClassFixture<AdminAuditLogEndpointTests.Factory>
{
    private const string TestSecret = "audit-log-test-secret-key-minimum-32-chars-padding!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public AdminAuditLogEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task ListAuditLogs_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/audit-logs");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListAuditLogs_RegularUser_Returns403WithEnvelope()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildClient(user.Id, user.SteamId, AuthRoles.User, []);

        var response = await client.GetAsync("/api/v1/admin/audit-logs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("INSUFFICIENT_PERMISSION",
            json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListAuditLogs_AdminWithoutPermission_Returns403()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_FLAGS"]);

        var response = await client.GetAsync("/api/v1/admin/audit-logs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListAuditLogs_AdminWithPermission_Returns_Paginated_Payload()
    {
        var admin = await _factory.CreateUserAsync();
        await _factory.SeedAuditRowsAsync(admin.Id);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_AUDIT_LOG"]);

        var response = await client.GetAsync("/api/v1/admin/audit-logs?pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.All(items, item =>
        {
            Assert.False(string.IsNullOrEmpty(item.GetProperty("category").GetString()));
            Assert.False(string.IsNullOrEmpty(item.GetProperty("action").GetString()));
            Assert.False(string.IsNullOrEmpty(item.GetProperty("actor").GetProperty("displayName").GetString()));
        });
    }

    [Fact]
    public async Task ListAuditLogs_Category_Filter_Restricts_Result_Set()
    {
        var admin = await _factory.CreateUserAsync();
        await _factory.SeedAuditRowsAsync(admin.Id);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_AUDIT_LOG"]);
        var response = await client.GetAsync("/api/v1/admin/audit-logs?category=ADMIN_ACTION");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.All(items, item =>
            Assert.Equal("ADMIN_ACTION", item.GetProperty("category").GetString()));
        Assert.Equal(items.Count, data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ListAuditLogs_TransactionId_Filter_Returns_Only_Matching_Tx()
    {
        var admin = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedAuditRowsAsync(admin.Id);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_AUDIT_LOG"]);
        var response = await client.GetAsync(
            $"/api/v1/admin/audit-logs?transactionId={transactionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        var item = data.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(transactionId.ToString(), item.GetProperty("transactionId").GetString());
    }

    [Fact]
    public async Task ListAuditLogs_SuperAdmin_Bypasses_Permission_Check()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin, []);
        var response = await client.GetAsync("/api/v1/admin/audit-logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient BuildClient(
        Guid userId, string steamId, string role, IReadOnlyList<string> permissions)
    {
        var token = IssueAccessToken(userId, steamId, role, permissions);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string IssueAccessToken(
        Guid userId, string steamId, string role, IReadOnlyList<string> permissions)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(AuthClaimTypes.UserId, userId.ToString()),
            new(AuthClaimTypes.SteamId, steamId),
            new(AuthClaimTypes.Role, role),
        };
        foreach (var permission in permissions)
            claims.Add(new Claim(AuthClaimTypes.Permission, permission));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = TestIssuer,
            Audience = TestAudience,
            Subject = new ClaimsIdentity(claims),
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
        private const string SteamIdPrefix = "76561198555042";

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public async Task<User> CreateUserAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Interlocked.Increment(ref _userSuffix);
            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = $"{SteamIdPrefix}{suffix:D3}",
                SteamDisplayName = $"T42Tester{suffix:D3}",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
            };
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        /// <summary>
        /// Seed three audit rows spanning all three categories. Returns the
        /// transactionId used in the FUND_MOVEMENT row so the transactionId
        /// filter test can target it.
        /// </summary>
        public async Task<Guid> SeedAuditRowsAsync(Guid actorAdminId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var transactionId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            db.Set<AuditLog>().AddRange(
                new AuditLog
                {
                    UserId = actorAdminId,
                    ActorId = actorAdminId,
                    ActorType = ActorType.ADMIN,
                    Action = AuditAction.SYSTEM_SETTING_CHANGED,
                    EntityType = "SystemSetting",
                    EntityId = "commission_rate",
                    NewValue = "{\"value\":\"0.03\"}",
                    CreatedAt = now,
                },
                new AuditLog
                {
                    UserId = actorAdminId,
                    ActorId = actorAdminId,
                    ActorType = ActorType.ADMIN,
                    Action = AuditAction.WALLET_ADDRESS_CHANGED,
                    EntityType = "User",
                    EntityId = actorAdminId.ToString(),
                    NewValue = "{\"address\":\"TXabc\"}",
                    CreatedAt = now,
                },
                new AuditLog
                {
                    UserId = null,
                    ActorId = SeedConstants.SystemUserId,
                    ActorType = ActorType.SYSTEM,
                    Action = AuditAction.WALLET_REFUND,
                    EntityType = "Transaction",
                    EntityId = transactionId.ToString(),
                    NewValue = "{\"amount\":\"5.00\"}",
                    CreatedAt = now.AddMinutes(-5),
                });
            await db.SaveChangesAsync();
            return transactionId;
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Set<AdminUserRole>().RemoveRange(
                db.Set<AdminUserRole>().IgnoreQueryFilters().ToList());
            db.Set<AdminRolePermission>().RemoveRange(
                db.Set<AdminRolePermission>().IgnoreQueryFilters().ToList());
            db.Set<AdminRole>().RemoveRange(
                db.Set<AdminRole>().IgnoreQueryFilters().ToList());

            db.Database.ExecuteSqlRaw("DELETE FROM AuditLogs");

            var seedIds = new[] { SeedConstants.SystemUserId };
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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }
}
