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
using Skinora.API.RateLimiting;
using Skinora.API.Outbox;
using Skinora.API.Services.UserSuspension;
using Skinora.API.Startup;
using Skinora.API.Tests.Common;
using Skinora.Auth.Configuration;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// Endpoint + job coverage for T105a account suspension — AD20
/// (<c>POST /admin/users/:userId/suspend</c>), AD21
/// (<c>DELETE /admin/users/:userId/suspend</c>), the <see cref="AutoUnsuspendJob"/>
/// temp-block sweep, and the <c>/auth/me</c> <c>isSuspended</c> flag.
/// </summary>
public class AdminUserSuspensionEndpointTests
    : IClassFixture<AdminUserSuspensionEndpointTests.Factory>
{
    private const string TestSecret = "admin-suspension-test-secret-key-minimum-32-chars!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public AdminUserSuspensionEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ---------- AD20 POST /admin/users/:userId/suspend ----------

    [Fact]
    public async Task SuspendUser_Unauthenticated_Returns401()
    {
        var target = await _factory.CreateUserAsync();
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/suspend",
            new { reason = "Multi-account fraud detected" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SuspendUser_NonAdmin_Returns403()
    {
        var actor = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync();
        var client = BuildClient(actor.Id, actor.SteamId, AuthRoles.User);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/suspend",
            new { reason = "Multi-account fraud detected" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuspendUser_AdminWithoutManageFlagsPermission_Returns403()
    {
        // Permission-granularity isolation: an Admin holding a DIFFERENT admin
        // permission (VIEW_FLAGS) but NOT MANAGE_FLAGS must be denied — proving the
        // gate enforces the specific permission, not merely the admin role.
        var actor = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync();
        var client = BuildClientWithPermissions(
            actor.Id, actor.SteamId, AuthRoles.Admin, ["VIEW_FLAGS"]);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/suspend",
            new { reason = "Multi-account fraud detected" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnsuspendUser_AdminWithoutManageFlagsPermission_Returns403()
    {
        // Same isolation on the DELETE un-suspend variant (also gated by MANAGE_FLAGS).
        var actor = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync(u => u.IsSuspended = true);
        var client = BuildClientWithPermissions(
            actor.Id, actor.SteamId, AuthRoles.Admin, ["VIEW_FLAGS"]);

        var response = await client.DeleteAsync($"/api/v1/admin/users/{target.Id}/suspend");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuspendUser_Permanent_Returns200_AndSetsFields()
    {
        var admin = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/suspend",
            new { reason = "Multi-account fraud detected", durationDays = (int?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal(target.Id.ToString(), data.GetProperty("userId").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("expiresAt").ValueKind);

        var persisted = await GetUserAsync(target.Id);
        Assert.True(persisted.IsSuspended);
        Assert.Equal("Multi-account fraud detected", persisted.SuspensionReason);
        Assert.NotNull(persisted.SuspendedAt);
        Assert.Null(persisted.SuspensionExpiresAt);
    }

    [Fact]
    public async Task SuspendUser_WithDurationDays_SetsExpiresAt()
    {
        var admin = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/suspend",
            new { reason = "Temporary block for review", durationDays = 7 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var persisted = await GetUserAsync(target.Id);
        Assert.True(persisted.IsSuspended);
        Assert.NotNull(persisted.SuspensionExpiresAt);
    }

    [Fact]
    public async Task SuspendUser_ShortReason_Returns400()
    {
        var admin = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/suspend",
            new { reason = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SuspendUser_LongReason_Returns400()
    {
        // Upper-bound guard (mirrors sibling AdminSanctionsService) — a reason
        // over the nvarchar(500) column returns a clean 400, not a SaveChanges 500.
        var admin = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/suspend",
            new { reason = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("error").GetProperty("code").GetString());
        Assert.False((await GetUserAsync(target.Id)).IsSuspended);
    }

    [Fact]
    public async Task SuspendUser_DurationDaysTooLarge_Returns400()
    {
        // Upper-bound guard — an absurd durationDays returns a clean 400 instead
        // of overflowing DateTime.AddDays into a 500 INTERNAL_ERROR.
        var admin = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/suspend",
            new { reason = "Temporary block for review", durationDays = int.MaxValue });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("error").GetProperty("code").GetString());
        Assert.False((await GetUserAsync(target.Id)).IsSuspended);
    }

    [Fact]
    public async Task SuspendUser_AlreadySuspended_Returns409()
    {
        var admin = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync(u => u.IsSuspended = true);
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/suspend",
            new { reason = "Multi-account fraud detected" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("ALREADY_SUSPENDED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SuspendUser_UnknownUser_Returns404()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/users/{Guid.NewGuid()}/suspend",
            new { reason = "Multi-account fraud detected" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- AD21 DELETE /admin/users/:userId/suspend ----------

    [Fact]
    public async Task UnsuspendUser_Returns200_AndClearsFields()
    {
        var admin = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync(u =>
        {
            u.IsSuspended = true;
            u.SuspendedAt = DateTime.UtcNow.AddDays(-1);
            u.SuspensionReason = "earlier suspension";
        });
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.DeleteAsync($"/api/v1/admin/users/{target.Id}/suspend");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var persisted = await GetUserAsync(target.Id);
        Assert.False(persisted.IsSuspended);
        Assert.Null(persisted.SuspensionReason);
        Assert.Null(persisted.SuspensionExpiresAt);
    }

    [Fact]
    public async Task UnsuspendUser_NotSuspended_Returns409()
    {
        var admin = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.DeleteAsync($"/api/v1/admin/users/{target.Id}/suspend");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("NOT_SUSPENDED", body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- /auth/me isSuspended ----------

    [Fact]
    public async Task Me_ReflectsSuspensionFlag()
    {
        var user = await _factory.CreateUserAsync(u => u.IsSuspended = true);
        var client = BuildClient(user.Id, user.SteamId, AuthRoles.User);

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.True(data.GetProperty("isSuspended").GetBoolean());
    }

    // ---------- AutoUnsuspendJob ----------

    [Fact]
    public async Task AutoUnsuspendJob_LiftsExpired_LeavesPermanentAndFuture()
    {
        var nowUtc = DateTime.UtcNow;
        var expired = await _factory.CreateUserAsync(u =>
        {
            u.IsSuspended = true;
            u.SuspendedAt = nowUtc.AddDays(-2);
            u.SuspensionReason = "temp";
            u.SuspensionExpiresAt = nowUtc.AddMinutes(-5);
        });
        var permanent = await _factory.CreateUserAsync(u =>
        {
            u.IsSuspended = true;
            u.SuspendedAt = nowUtc.AddDays(-2);
            u.SuspensionReason = "permanent";
            u.SuspensionExpiresAt = null;
        });
        var future = await _factory.CreateUserAsync(u =>
        {
            u.IsSuspended = true;
            u.SuspendedAt = nowUtc;
            u.SuspensionReason = "temp-future";
            u.SuspensionExpiresAt = nowUtc.AddDays(1);
        });

        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<AutoUnsuspendJob>();
            var lifted = await job.ExecuteAsync(CancellationToken.None);
            Assert.Equal(1, lifted);
        }

        Assert.False((await GetUserAsync(expired.Id)).IsSuspended);
        Assert.True((await GetUserAsync(permanent.Id)).IsSuspended);
        Assert.True((await GetUserAsync(future.Id)).IsSuspended);
    }

    // ---------- side effects: audit + notification event (AC6) ----------

    [Fact]
    public async Task SuspendUser_WritesAudit_AndPublishesNotificationEvent()
    {
        var admin = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/suspend",
            new { reason = "Multi-account fraud detected" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var audits = await GetAuditLogsForUserAsync(target.Id);
        Assert.Contains(audits, a => a.Action == AuditAction.USER_BANNED);

        var outbox = await GetOutboxAsync();
        Assert.Contains(outbox, m => m.EventType.Contains("AccountSuspendedEvent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnsuspendUser_WritesAudit_AndPublishesNotificationEvent()
    {
        var admin = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync(u =>
        {
            u.IsSuspended = true;
            u.SuspendedAt = DateTime.UtcNow.AddDays(-1);
            u.SuspensionReason = "earlier suspension";
        });
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.DeleteAsync($"/api/v1/admin/users/{target.Id}/suspend");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var audits = await GetAuditLogsForUserAsync(target.Id);
        Assert.Contains(audits, a => a.Action == AuditAction.USER_UNBANNED);

        var outbox = await GetOutboxAsync();
        Assert.Contains(outbox, m => m.EventType.Contains("AccountUnsuspendedEvent", StringComparison.Ordinal));
    }

    // ---------- helpers ----------

    private async Task<User> GetUserAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<User>().AsNoTracking().IgnoreQueryFilters().SingleAsync(u => u.Id == id);
    }

    private async Task<List<AuditLog>> GetAuditLogsForUserAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<AuditLog>().AsNoTracking().Where(a => a.UserId == userId).ToListAsync();
    }

    private async Task<List<OutboxMessage>> GetOutboxAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<OutboxMessage>().AsNoTracking().ToListAsync();
    }

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

    // WP18 — permission-aware token for the granular-permission isolation tests
    // only. Kept SEPARATE from the role-only BuildClient/IssueAccessToken above so
    // the ~13 existing call sites stay untouched (SuperAdmin bypasses, plain User
    // is denied by role, so none of them need a permission claim).
    private HttpClient BuildClientWithPermissions(
        Guid userId, string steamId, string role, IReadOnlyList<string> permissions)
    {
        var token = IssueAccessTokenWithPermissions(userId, steamId, role, permissions);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string IssueAccessTokenWithPermissions(
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
        private const string SteamIdPrefix = "76561198556222";

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

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Database.ExecuteSqlRaw("DELETE FROM AuditLogs");
            db.Database.ExecuteSqlRaw("DELETE FROM OutboxMessages");

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
