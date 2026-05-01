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

/// <summary>Integration tests for 07 §9.11–§9.14 (T39 — admin role CRUD).</summary>
public class AdminRolesEndpointTests : IClassFixture<AdminRolesEndpointTests.Factory>
{
    private const string TestSecret = "admin-roles-test-secret-key-minimum-32-chars!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public AdminRolesEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ---------- AD11 GET /admin/roles ----------

    [Fact]
    public async Task ListRoles_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/roles");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListRoles_NonAdmin_Returns403()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildClient(user.Id, user.SteamId, AuthRoles.User);

        var response = await client.GetAsync("/api/v1/admin/roles");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListRoles_SuperAdmin_ReturnsRolesAndCatalog()
    {
        var admin = await _factory.CreateUserAsync();
        var role = await _factory.CreateRoleAsync("Flag Yöneticisi", "Flag yönetimi",
            ["VIEW_FLAGS", "MANAGE_FLAGS"]);
        await _factory.AssignRoleAsync(admin.Id, role.Id);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.GetAsync("/api/v1/admin/roles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");

        var available = data.GetProperty("availablePermissions");
        Assert.Equal(11, available.GetArrayLength());
        var keys = new List<string>();
        foreach (var entry in available.EnumerateArray())
            keys.Add(entry.GetProperty("key").GetString()!);
        Assert.Contains("MANAGE_STEAM_RECOVERY", keys);
        Assert.Contains("MANAGE_ROLES", keys);
        Assert.Contains("EMERGENCY_HOLD", keys);

        var roles = data.GetProperty("roles");
        Assert.Equal(1, roles.GetArrayLength());
        var first = roles[0];
        Assert.Equal("Flag Yöneticisi", first.GetProperty("name").GetString());
        Assert.Equal(1, first.GetProperty("assignedUserCount").GetInt32());
        var perms = first.GetProperty("permissions");
        Assert.Equal(2, perms.GetArrayLength());
    }

    // ---------- AD12 POST /admin/roles ----------

    [Fact]
    public async Task CreateRole_Valid_Returns201WithDetail()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync("/api/v1/admin/roles", new
        {
            name = "İşlem Denetçisi",
            description = "İşlemleri görüntüleyebilir",
            permissions = new[] { "VIEW_TRANSACTIONS", "VIEW_FLAGS" },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("İşlem Denetçisi", data.GetProperty("name").GetString());
        Assert.False(data.GetProperty("isSuperAdmin").GetBoolean());
        var perms = data.GetProperty("permissions");
        Assert.Equal(2, perms.GetArrayLength());
    }

    [Fact]
    public async Task CreateRole_DuplicateName_Returns409RoleNameExists()
    {
        var admin = await _factory.CreateUserAsync();
        await _factory.CreateRoleAsync("Flag Yöneticisi", null, []);
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync("/api/v1/admin/roles", new
        {
            name = "Flag Yöneticisi",
            description = (string?)null,
            permissions = Array.Empty<string>(),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("ROLE_NAME_EXISTS",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateRole_EmptyName_Returns400ValidationError()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync("/api/v1/admin/roles", new
        {
            name = "  ",
            description = (string?)null,
            permissions = Array.Empty<string>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("VALIDATION_ERROR",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateRole_UnknownPermission_Returns400InvalidPermission()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync("/api/v1/admin/roles", new
        {
            name = "Yeni rol",
            description = (string?)null,
            permissions = new[] { "VIEW_FLAGS", "DOES_NOT_EXIST" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("INVALID_PERMISSION",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- AD13 PUT /admin/roles/:id ----------

    [Fact]
    public async Task UpdateRole_ChangesNameDescriptionAndPermissions()
    {
        var admin = await _factory.CreateUserAsync();
        var role = await _factory.CreateRoleAsync("Eski İsim", "eski açıklama",
            ["VIEW_FLAGS"]);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.PutAsJsonAsync($"/api/v1/admin/roles/{role.Id}", new
        {
            name = "Yeni İsim",
            description = "yeni açıklama",
            permissions = new[] { "MANAGE_FLAGS", "VIEW_AUDIT_LOG" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("Yeni İsim", data.GetProperty("name").GetString());
        Assert.Equal("yeni açıklama", data.GetProperty("description").GetString());

        var permissions = await _factory.GetRolePermissionsAsync(role.Id);
        Assert.Equal(2, permissions.Count);
        Assert.Contains("MANAGE_FLAGS", permissions);
        Assert.Contains("VIEW_AUDIT_LOG", permissions);
        Assert.DoesNotContain("VIEW_FLAGS", permissions);
    }

    [Fact]
    public async Task UpdateRole_UnknownId_Returns404RoleNotFound()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PutAsJsonAsync($"/api/v1/admin/roles/{Guid.NewGuid()}", new
        {
            name = "X",
            description = (string?)null,
            permissions = Array.Empty<string>(),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("ROLE_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- AD14 DELETE /admin/roles/:id ----------

    [Fact]
    public async Task DeleteRole_Unassigned_Returns200AndSoftDeletes()
    {
        var admin = await _factory.CreateUserAsync();
        var role = await _factory.CreateRoleAsync("Geçici", null, ["VIEW_FLAGS"]);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.DeleteAsync($"/api/v1/admin/roles/{role.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await _factory.RoleIsLiveAsync(role.Id));
    }

    [Fact]
    public async Task DeleteRole_AssignedToUser_Returns422RoleHasUsers()
    {
        var admin = await _factory.CreateUserAsync();
        var assignedUser = await _factory.CreateUserAsync();
        var role = await _factory.CreateRoleAsync("Aktif Rol", null, []);
        await _factory.AssignRoleAsync(assignedUser.Id, role.Id);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.DeleteAsync($"/api/v1/admin/roles/{role.Id}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("ROLE_HAS_USERS",
            body.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1,
            body.GetProperty("error").GetProperty("details")
                .GetProperty("assignedUserCount").GetInt32());

        Assert.True(await _factory.RoleIsLiveAsync(role.Id));
    }

    [Fact]
    public async Task DeleteRole_UnknownId_Returns404RoleNotFound()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.DeleteAsync($"/api/v1/admin/roles/{Guid.NewGuid()}");

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
        private const string SteamIdPrefix = "76561198555444";

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

        public async Task<AdminRole> CreateRoleAsync(
            string name, string? description, IReadOnlyList<string> permissions)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = new AdminRole
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = description,
                IsSuperAdmin = false,
            };
            db.Set<AdminRole>().Add(role);

            foreach (var key in permissions)
            {
                db.Set<AdminRolePermission>().Add(new AdminRolePermission
                {
                    Id = Guid.NewGuid(),
                    AdminRoleId = role.Id,
                    Permission = key,
                });
            }
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

        public async Task<List<string>> GetRolePermissionsAsync(Guid roleId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<AdminRolePermission>()
                .AsNoTracking()
                .Where(p => p.AdminRoleId == roleId)
                .Select(p => p.Permission)
                .ToListAsync();
        }

        public async Task<bool> RoleIsLiveAsync(Guid roleId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<AdminRole>()
                .AsNoTracking()
                .AnyAsync(r => r.Id == roleId);
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
