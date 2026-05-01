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
using Skinora.Admin.Domain.Entities;
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Startup;
using Skinora.API.Tests.Common;
using Skinora.Auth.Configuration;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T41 — End-to-end coverage for <c>GET /api/v1/admin/settings</c> and
/// <c>PUT /api/v1/admin/settings/:key</c> (07 §9.8–§9.9). Mirrors
/// <see cref="AdminRbacEndpointTests"/>'s SQLite + JWT factory pattern so the
/// MANAGE_SETTINGS permission gate, controller wiring, validator surface and
/// AuditLog INSERT all exercise the real DI graph.
/// </summary>
public class AdminSettingsEndpointTests : IClassFixture<AdminSettingsEndpointTests.Factory>
{
    private const string TestSecret = "admin-settings-test-secret-key-minimum-32-chars!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public AdminSettingsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task ListSettings_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListSettings_RegularUser_Returns403WithEnvelope()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildClient(user.Id, user.SteamId, AuthRoles.User, []);

        var response = await client.GetAsync("/api/v1/admin/settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("INSUFFICIENT_PERMISSION",
            json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListSettings_AdminWithManageSettings_Returns200WithCatalogPayload()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);

        var response = await client.GetAsync("/api/v1/admin/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // ApiResponseWrapperFilter wraps success bodies in
        // { success: true, data: <payload>, traceId } — peel one layer.
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
        var settings = data.GetProperty("settings").EnumerateArray().ToList();
        Assert.NotEmpty(settings);

        var commission = settings.Single(s => s.GetProperty("key").GetString() == "commission_rate");
        Assert.Equal("0.02", commission.GetProperty("value").GetString());
        Assert.Equal("commission", commission.GetProperty("category").GetString());
        Assert.Equal("number", commission.GetProperty("valueType").GetString());
        Assert.False(string.IsNullOrEmpty(commission.GetProperty("label").GetString()));
    }

    [Fact]
    public async Task ListSettings_SuperAdmin_BypassesAndReturns200()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin, []);

        var response = await client.GetAsync("/api/v1/admin/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSetting_AdminWithManageSettings_Returns200_And_Persists_Audit()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);

        var response = await client.PutAsJsonAsync(
            "/api/v1/admin/settings/commission_rate",
            new { value = "0.03" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
        Assert.Equal("commission_rate", data.GetProperty("key").GetString());
        Assert.Equal("0.03", data.GetProperty("value").GetString());

        // DB side: setting updated AND audit row inserted.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var setting = await db.Set<SystemSetting>()
            .AsNoTracking()
            .FirstAsync(s => s.Key == "commission_rate");
        Assert.Equal("0.03", setting.Value);
        Assert.Equal(admin.Id, setting.UpdatedByAdminId);

        var audit = await db.Set<AuditLog>()
            .AsNoTracking()
            .OrderByDescending(a => a.Id)
            .FirstAsync(a => a.EntityType == "SystemSetting" && a.EntityId == "commission_rate");
        Assert.Equal(AuditAction.SYSTEM_SETTING_CHANGED, audit.Action);
        Assert.Equal(ActorType.ADMIN, audit.ActorType);
        Assert.Equal(admin.Id, audit.ActorId);
        Assert.Contains("0.03", audit.NewValue);
    }

    [Fact]
    public async Task UpdateSetting_UnknownKey_Returns404()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);

        var response = await client.PutAsJsonAsync(
            "/api/v1/admin/settings/made_up_setting",
            new { value = "1" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("SETTING_NOT_FOUND",
            json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateSetting_BadValue_Returns400_With_Validation_Error()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);

        var response = await client.PutAsJsonAsync(
            "/api/v1/admin/settings/commission_rate",
            new { value = "1.5" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("VALIDATION_ERROR",
            json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateSetting_MissingPermission_Returns403()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_FLAGS"]);

        var response = await client.PutAsJsonAsync(
            "/api/v1/admin/settings/commission_rate",
            new { value = "0.03" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
        private const string SteamIdPrefix = "76561198555041";

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
                SteamDisplayName = $"T41Tester{suffix:D3}",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
            };
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
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

            // Audit table is append-only at the DbContext level — bulk delete via raw SQL
            // bypasses change tracker, so EnforceAppendOnly does not fire. SQLite supports
            // DELETE FROM directly.
            db.Database.ExecuteSqlRaw("DELETE FROM AuditLogs");

            // Reset commission_rate to its seeded default so update tests are reproducible.
            db.Database.ExecuteSqlRaw(
                "UPDATE SystemSettings SET Value = '0.02', IsConfigured = 1, UpdatedByAdminId = NULL " +
                "WHERE [Key] = 'commission_rate'");

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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }
}
