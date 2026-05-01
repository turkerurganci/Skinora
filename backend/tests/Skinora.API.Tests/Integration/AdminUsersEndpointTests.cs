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
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>Integration tests for 07 §9.15–§9.18 (T39 — admin users + role assignment).</summary>
public class AdminUsersEndpointTests : IClassFixture<AdminUsersEndpointTests.Factory>
{
    private const string TestSecret = "admin-users-test-secret-key-minimum-32-chars!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public AdminUsersEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ---------- AD15 GET /admin/users ----------

    [Fact]
    public async Task ListUsers_NoParams_ReturnsOnlyUsersWithRoles()
    {
        var caller = await _factory.CreateUserAsync(displayName: "Caller");
        var assigned = await _factory.CreateUserAsync(displayName: "AssignedAdmin");
        await _factory.CreateUserAsync(displayName: "RegularUser");

        var role = await _factory.CreateRoleAsync("Flag Yöneticisi");
        await _factory.AssignRoleAsync(assigned.Id, role.Id);

        var client = BuildClient(caller.Id, caller.SteamId, AuthRoles.SuperAdmin);
        var response = await client.GetAsync("/api/v1/admin/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        var items = data.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("AssignedAdmin", items[0].GetProperty("displayName").GetString());
        Assert.Equal(role.Id.ToString(),
            items[0].GetProperty("role").GetProperty("id").GetString());
    }

    [Fact]
    public async Task ListUsers_RoleIdFilter_RestrictsToRole()
    {
        var caller = await _factory.CreateUserAsync(displayName: "Caller");
        var u1 = await _factory.CreateUserAsync(displayName: "User1");
        var u2 = await _factory.CreateUserAsync(displayName: "User2");

        var role1 = await _factory.CreateRoleAsync("Rol 1");
        var role2 = await _factory.CreateRoleAsync("Rol 2");
        await _factory.AssignRoleAsync(u1.Id, role1.Id);
        await _factory.AssignRoleAsync(u2.Id, role2.Id);

        var client = BuildClient(caller.Id, caller.SteamId, AuthRoles.SuperAdmin);
        var response = await client.GetAsync($"/api/v1/admin/users?roleId={role1.Id}");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var items = body.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("User1", items[0].GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task ListUsers_SearchBroadensToNonAdmins()
    {
        var caller = await _factory.CreateUserAsync(displayName: "Caller");
        var nonAdmin = await _factory.CreateUserAsync(displayName: "Searchable");
        await _factory.CreateUserAsync(displayName: "Other");

        var client = BuildClient(caller.Id, caller.SteamId, AuthRoles.SuperAdmin);
        var response = await client.GetAsync("/api/v1/admin/users?search=Searchable");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var items = body.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(nonAdmin.Id.ToString(), items[0].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("role").ValueKind);
    }

    [Fact]
    public async Task ListUsers_NonAdmin_Returns403()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildClient(user.Id, user.SteamId, AuthRoles.User);

        var response = await client.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- AD16 GET /admin/users/:steamId ----------

    [Fact]
    public async Task GetUserDetail_Active_ReturnsProfileAndStatus()
    {
        var admin = await _factory.CreateUserAsync(displayName: "Admin");
        var target = await _factory.CreateUserAsync(displayName: "TargetUser", c =>
        {
            c.DefaultPayoutAddress = "TXyzSeller";
            c.PayoutAddressChangedAt = DateTime.UtcNow.AddDays(-3);
            c.DefaultRefundAddress = "TAbcBuyer";
            c.RefundAddressChangedAt = DateTime.UtcNow.AddDays(-5);
        });

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.GetAsync($"/api/v1/admin/users/{target.SteamId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");

        var profile = data.GetProperty("profile");
        Assert.Equal("ACTIVE", profile.GetProperty("accountStatus").GetString());
        Assert.Equal("TargetUser", profile.GetProperty("displayName").GetString());

        var wallet = data.GetProperty("walletHistory");
        Assert.Equal(2, wallet.GetArrayLength());

        // T39 forward devir — empty placeholders for downstream wiring.
        Assert.Equal(0, data.GetProperty("flagHistory").GetArrayLength());
        Assert.Equal(0, data.GetProperty("disputeHistory").GetArrayLength());
        Assert.Equal(0, data.GetProperty("frequentCounterparties").GetArrayLength());
    }

    [Fact]
    public async Task GetUserDetail_Deactivated_ReturnsDeactivatedStatus()
    {
        var admin = await _factory.CreateUserAsync(displayName: "Admin");
        var target = await _factory.CreateUserAsync(displayName: "Frozen", c =>
        {
            c.IsDeactivated = true;
            c.DeactivatedAt = DateTime.UtcNow.AddHours(-1);
        });

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.GetAsync($"/api/v1/admin/users/{target.SteamId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("DEACTIVATED",
            body.GetProperty("data").GetProperty("profile")
                .GetProperty("accountStatus").GetString());
    }

    [Fact]
    public async Task GetUserDetail_UnknownSteamId_Returns404()
    {
        var admin = await _factory.CreateUserAsync(displayName: "Admin");
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.GetAsync("/api/v1/admin/users/76561190000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("USER_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- AD16b GET /admin/users/:steamId/transactions ----------

    [Fact]
    public async Task GetUserTransactions_Existing_ReturnsEmptyPagedResult()
    {
        var admin = await _factory.CreateUserAsync(displayName: "Admin");
        var target = await _factory.CreateUserAsync(displayName: "Target");

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.GetAsync(
            $"/api/v1/admin/users/{target.SteamId}/transactions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, data.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task GetUserTransactions_UnknownSteamId_Returns404()
    {
        var admin = await _factory.CreateUserAsync(displayName: "Admin");
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.GetAsync(
            "/api/v1/admin/users/76561190000000999/transactions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("USER_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- AD17 PUT /admin/users/:id/role ----------

    [Fact]
    public async Task AssignRole_NewAssignment_Returns200WithRoleAndAssignedAt()
    {
        var admin = await _factory.CreateUserAsync(displayName: "Admin");
        var target = await _factory.CreateUserAsync(displayName: "Target");
        var role = await _factory.CreateRoleAsync("İşlem Denetçisi");

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.PutAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/role", new { roleId = role.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(target.Id.ToString(), data.GetProperty("userId").GetString());
        Assert.Equal(role.Id.ToString(),
            data.GetProperty("role").GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.String, data.GetProperty("assignedAt").ValueKind);

        var current = await _factory.GetActiveAssignmentRoleIdsAsync(target.Id);
        Assert.Single(current);
        Assert.Equal(role.Id, current[0]);
    }

    [Fact]
    public async Task AssignRole_ReplaceExistingRole_TombstonesPriorAssignment()
    {
        var admin = await _factory.CreateUserAsync(displayName: "Admin");
        var target = await _factory.CreateUserAsync(displayName: "Target");
        var first = await _factory.CreateRoleAsync("İlk Rol");
        var second = await _factory.CreateRoleAsync("İkinci Rol");

        await _factory.AssignActiveRoleAsync(target.Id, first.Id);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.PutAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/role", new { roleId = second.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var current = await _factory.GetActiveAssignmentRoleIdsAsync(target.Id);
        Assert.Single(current);
        Assert.Equal(second.Id, current[0]);

        var allIncludingTombstoned = await _factory.GetAllAssignmentRoleIdsAsync(target.Id);
        Assert.Equal(2, allIncludingTombstoned.Count);
    }

    [Fact]
    public async Task AssignRole_NullRoleId_ClearsAssignment()
    {
        var admin = await _factory.CreateUserAsync(displayName: "Admin");
        var target = await _factory.CreateUserAsync(displayName: "Target");
        var role = await _factory.CreateRoleAsync("Var olan Rol");
        await _factory.AssignActiveRoleAsync(target.Id, role.Id);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.PutAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/role", new { roleId = (Guid?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("role").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("assignedAt").ValueKind);

        var current = await _factory.GetActiveAssignmentRoleIdsAsync(target.Id);
        Assert.Empty(current);
    }

    [Fact]
    public async Task AssignRole_UnknownUser_Returns404UserNotFound()
    {
        var admin = await _factory.CreateUserAsync(displayName: "Admin");
        var role = await _factory.CreateRoleAsync("Rol");

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.PutAsJsonAsync(
            $"/api/v1/admin/users/{Guid.NewGuid()}/role", new { roleId = role.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("USER_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AssignRole_UnknownRole_Returns404RoleNotFound()
    {
        var admin = await _factory.CreateUserAsync(displayName: "Admin");
        var target = await _factory.CreateUserAsync(displayName: "Target");

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.PutAsJsonAsync(
            $"/api/v1/admin/users/{target.Id}/role", new { roleId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("ROLE_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
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
        private int _roleSuffix;
        private const string SteamIdPrefix = "76561198777666";

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public async Task<User> CreateUserAsync(
            string? displayName = null, Action<User>? customize = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Interlocked.Increment(ref _userSuffix);
            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = $"{SteamIdPrefix}{suffix:D3}",
                SteamDisplayName = displayName ?? $"Tester{suffix:D3}",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
            };
            customize?.Invoke(user);

            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public async Task<AdminRole> CreateRoleAsync(string? name = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Interlocked.Increment(ref _roleSuffix);
            var role = new AdminRole
            {
                Id = Guid.NewGuid(),
                Name = name ?? $"Role-{suffix}",
                Description = null,
                IsSuperAdmin = false,
            };
            db.Set<AdminRole>().Add(role);
            await db.SaveChangesAsync();
            return role;
        }

        public async Task AssignRoleAsync(Guid userId, Guid roleId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<AdminUserRole>().Add(new AdminUserRole
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AdminRoleId = roleId,
                AssignedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public Task AssignActiveRoleAsync(Guid userId, Guid roleId)
            => AssignRoleAsync(userId, roleId);

        public async Task<List<Guid>> GetActiveAssignmentRoleIdsAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<AdminUserRole>()
                .AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.AdminRoleId)
                .ToListAsync();
        }

        public async Task<List<Guid>> GetAllAssignmentRoleIdsAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<AdminUserRole>()
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.AdminRoleId)
                .ToListAsync();
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
