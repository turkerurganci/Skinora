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
/// Integration tests for T82 admin sanctions endpoints (07 §9.23–§9.25
/// AD22 / AD23 / AD24, 02 §21.1, 03 §11a.3, 06 §3.25).
/// </summary>
public class AdminSanctionsEndpointTests
    : IClassFixture<AdminSanctionsEndpointTests.Factory>
{
    private const string TestSecret = "admin-sanctions-test-secret-key-minimum-32-chars!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string SteamIdPrefix = "76561198777666";

    // 34-char, T-prefixed, Base58 (alphabet: 123456789ABCDEFGHJKLMNPQRSTUVWXYZ
    // abcdefghijkmnopqrstuvwxyz — excludes 0/O/I/l).
    private const string SanctionedAddress1 = "TtestSnc123456789abcdefghJKMNPQRSt";
    private const string SanctionedAddress2 = "TtestSnc9876541abcdefghZYXWVUmnpqr";
    private const string UnsanctionedAddress = "TtestCln123456789abcdefghJKMNPQRSt";
    private const string InvalidShortAddress = "TShort";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public AdminSanctionsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ---------- AD22 GET /admin/sanctions/addresses ----------

    [Fact]
    public async Task ListAddresses_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/sanctions/addresses");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListAddresses_NonAdmin_Returns403()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildClient(user.Id, user.SteamId, AuthRoles.User);

        var response = await client.GetAsync("/api/v1/admin/sanctions/addresses");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListAddresses_AdminWithoutPermission_Returns403()
    {
        var admin = await _factory.CreateUserAsync();
        // Admin role that does NOT include MANAGE_SANCTIONS.
        var role = await _factory.CreateRoleAsync("View Only", null, ["VIEW_FLAGS"]);
        await _factory.AssignRoleAsync(admin.Id, role.Id);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin);
        var response = await client.GetAsync("/api/v1/admin/sanctions/addresses");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListAddresses_SuperAdmin_ReturnsActiveOnlyByDefault()
    {
        var admin = await _factory.CreateUserAsync();
        await _factory.AddSanctionedAddressAsync(SanctionedAddress1, admin.Id, isActive: true);
        await _factory.AddSanctionedAddressAsync(SanctionedAddress2, admin.Id, isActive: false);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.GetAsync("/api/v1/admin/sanctions/addresses");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        var items = data.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(SanctionedAddress1, items[0].GetProperty("address").GetString());
        Assert.True(items[0].GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task ListAddresses_IsActiveFalse_ReturnsInactiveOnly()
    {
        var admin = await _factory.CreateUserAsync();
        await _factory.AddSanctionedAddressAsync(SanctionedAddress1, admin.Id, isActive: true);
        await _factory.AddSanctionedAddressAsync(SanctionedAddress2, admin.Id, isActive: false);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.GetAsync(
            "/api/v1/admin/sanctions/addresses?isActive=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        var items = data.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(SanctionedAddress2, items[0].GetProperty("address").GetString());
        Assert.False(items[0].GetProperty("isActive").GetBoolean());
    }

    // ---------- AD23 POST /admin/sanctions/addresses ----------

    [Fact]
    public async Task AddAddress_Valid_Returns201AndPersists()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/sanctions/addresses",
            new
            {
                address = SanctionedAddress1,
                network = "TRC-20",
                source = "MANUAL",
                reason = "Test bildirim no. 2026-05-19",
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(SanctionedAddress1, data.GetProperty("address").GetString());
        Assert.Equal("MANUAL", data.GetProperty("source").GetString());
        Assert.True(data.GetProperty("isActive").GetBoolean());

        // Persisted.
        var live = await _factory.GetActiveSanctionsCountAsync(SanctionedAddress1);
        Assert.Equal(1, live);
    }

    [Fact]
    public async Task AddAddress_InvalidTrc20_Returns400InvalidWalletAddress()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/sanctions/addresses",
            new
            {
                address = InvalidShortAddress,
                network = "TRC-20",
                source = "MANUAL",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("INVALID_WALLET_ADDRESS",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddAddress_DuplicateActive_Returns409AlreadyListed()
    {
        var admin = await _factory.CreateUserAsync();
        await _factory.AddSanctionedAddressAsync(SanctionedAddress1, admin.Id, isActive: true);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/sanctions/addresses",
            new
            {
                address = SanctionedAddress1,
                network = "TRC-20",
                source = "MANUAL",
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("SANCTIONS_ADDRESS_ALREADY_LISTED",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddAddress_RetroactiveScan_FlagsExistingUserWithSanctionedWallet()
    {
        var admin = await _factory.CreateUserAsync();
        var holder = await _factory.CreateUserAsync(u =>
        {
            u.DefaultPayoutAddress = SanctionedAddress1;
        });

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/sanctions/addresses",
            new
            {
                address = SanctionedAddress1,
                network = "TRC-20",
                source = "MANUAL",
                reason = "Retroactive scan test",
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var fraudFlagged = await _factory.HasPendingSanctionsFlagAsync(holder.Id);
        Assert.True(fraudFlagged,
            "Retroactive scan should have staged a PENDING account-level SANCTIONS_MATCH flag.");
    }

    // ---------- AD24 DELETE /admin/sanctions/addresses/:id ----------

    [Fact]
    public async Task DeactivateAddress_Active_Returns200AndSoftDeactivates()
    {
        var admin = await _factory.CreateUserAsync();
        var sanctionedId = await _factory.AddSanctionedAddressAsync(
            SanctionedAddress1, admin.Id, isActive: true);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.DeleteAsync(
            $"/api/v1/admin/sanctions/addresses/{sanctionedId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.False(data.GetProperty("isActive").GetBoolean());

        // Filtered UQ allows re-adding the same address.
        Assert.Equal(0, await _factory.GetActiveSanctionsCountAsync(SanctionedAddress1));
    }

    [Fact]
    public async Task DeactivateAddress_AlreadyInactive_Returns409AlreadyInactive()
    {
        var admin = await _factory.CreateUserAsync();
        var sanctionedId = await _factory.AddSanctionedAddressAsync(
            SanctionedAddress1, admin.Id, isActive: false);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);
        var response = await client.DeleteAsync(
            $"/api/v1/admin/sanctions/addresses/{sanctionedId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("SANCTIONS_ADDRESS_ALREADY_INACTIVE",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task DeactivateAddress_NotFound_Returns404()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin);

        var response = await client.DeleteAsync(
            $"/api/v1/admin/sanctions/addresses/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("SANCTIONS_ADDRESS_NOT_FOUND",
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
                SteamDisplayName = $"SanctionsTester{suffix:D3}",
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

        public async Task<Guid> AddSanctionedAddressAsync(
            string address, Guid adminId, bool isActive)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var nowUtc = DateTime.UtcNow;
            var entity = new SanctionedAddress
            {
                Id = Guid.NewGuid(),
                Address = address,
                Network = SanctionedAddressNetworks.Trc20,
                Source = SanctionedAddressSources.Manual,
                Reason = "Test seed",
                ListedAt = nowUtc,
                AddedByAdminId = adminId,
                IsActive = isActive,
            };
            db.Set<SanctionedAddress>().Add(entity);
            await db.SaveChangesAsync();
            return entity.Id;
        }

        public async Task<int> GetActiveSanctionsCountAsync(string address)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<SanctionedAddress>()
                .AsNoTracking()
                .CountAsync(s => s.IsActive && s.Address == address);
        }

        public async Task<bool> HasPendingSanctionsFlagAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<Skinora.Fraud.Domain.Entities.FraudFlag>()
                .AsNoTracking()
                .AnyAsync(f =>
                    f.UserId == userId
                    && f.Type == FraudFlagType.SANCTIONS_MATCH
                    && f.Scope == FraudFlagScope.ACCOUNT_LEVEL
                    && f.Status == ReviewStatus.PENDING
                    && !f.IsDeleted);
        }

        public void Reset()
        {
            // Full schema rebuild — granular RemoveRange is fragile across the
            // 30+ entity graph with FK / RowVersion concurrency interactions.
            // EnsureDeleted + EnsureCreated restores the SqliteConnection's
            // in-memory schema with seed data in <50ms.
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
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
