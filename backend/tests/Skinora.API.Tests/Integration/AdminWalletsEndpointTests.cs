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
using Skinora.API.BackgroundJobs;
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Startup;
using Skinora.API.Tests.Common;
using Skinora.Auth.Configuration;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Wallets;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// HTTP-boundary coverage for <c>POST /api/v1/admin/wallets/hot-to-cold-transfer</c>
/// (T77 — 05 §3.3). Mirrors <see cref="AdminSettingsEndpointTests"/>'s SQLite + JWT
/// factory (same MANAGE_SETTINGS gate) and stubs <see cref="IHotWalletService"/> at
/// the boundary, so the auth policy, token/amount validation, outcome → status
/// mapping and success envelope are exercised against the real DI graph. The
/// service-level DB writes stay covered by the unit-level HotWalletServiceTests.
/// </summary>
public class AdminWalletsEndpointTests : IClassFixture<AdminWalletsEndpointTests.Factory>
{
    private const string TestSecret = "admin-wallets-test-secret-key-minimum-32-chars!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string Route = "/api/v1/admin/wallets/hot-to-cold-transfer";

    private readonly Factory _factory;

    public AdminWalletsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task InitiateColdTransfer_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Route, new { amount = 100m, token = "USDT" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InitiateColdTransfer_AdminWithoutManageSettings_Returns403()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_FLAGS"]);

        var response = await client.PostAsJsonAsync(Route, new { amount = 100m, token = "USDT" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InitiateColdTransfer_Success_Returns200_WithEnvelope()
    {
        _factory.HotWalletFake.SetSuccess(42L, "0xhash", 100m, StablecoinType.USDT, "THotAddr", "TColdAddr");
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);

        var response = await client.PostAsJsonAsync(Route, new { amount = 100m, token = "USDT" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data");
        Assert.Equal(42, data.GetProperty("coldTransferId").GetInt64());
        Assert.Equal("0xhash", data.GetProperty("txHash").GetString());
        Assert.Equal("100", data.GetProperty("amount").GetString());
        Assert.Equal("USDT", data.GetProperty("token").GetString());
        Assert.Equal("THotAddr", data.GetProperty("fromAddress").GetString());
        Assert.Equal("TColdAddr", data.GetProperty("toAddress").GetString());

        // The controller forwarded the parsed token + caller id to the service.
        Assert.Equal(StablecoinType.USDT, _factory.HotWalletFake.LastToken);
        Assert.Equal(admin.Id, _factory.HotWalletFake.LastAdminId);
    }

    [Fact]
    public async Task InitiateColdTransfer_UnknownToken_Returns400_BeforeService()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);

        // 'DOGE' is not a StablecoinType — Enum.TryParse(ignoreCase:false) short-circuits.
        var response = await client.PostAsJsonAsync(Route, new { amount = 100m, token = "DOGE" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_TOKEN", await ErrorCode(response));
    }

    [Theory]
    [InlineData("INVALID_AMOUNT", HttpStatusCode.BadRequest)]
    [InlineData("HOT_WALLET_NOT_CONFIGURED", HttpStatusCode.UnprocessableEntity)]
    [InlineData("COLD_WALLET_NOT_CONFIGURED", HttpStatusCode.UnprocessableEntity)]
    [InlineData("SIDECAR_UNAVAILABLE", HttpStatusCode.BadGateway)]
    public async Task InitiateColdTransfer_ServiceOutcome_MapsToStatusAndCode(
        string expectedCode, HttpStatusCode expectedStatus)
    {
        switch (expectedCode)
        {
            case "INVALID_AMOUNT":
                _factory.HotWalletFake.SetInvalidAmount("Amount must be greater than zero.");
                break;
            case "HOT_WALLET_NOT_CONFIGURED":
                _factory.HotWalletFake.SetHotWalletNotConfigured();
                break;
            case "COLD_WALLET_NOT_CONFIGURED":
                _factory.HotWalletFake.SetColdWalletNotConfigured();
                break;
            default:
                _factory.HotWalletFake.SetSidecarUnavailable("DEGRADED");
                break;
        }

        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);

        var response = await client.PostAsJsonAsync(Route, new { amount = 100m, token = "USDT" });

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedCode, await ErrorCode(response));
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return json.GetProperty("error").GetProperty("code").GetString();
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

    public sealed class FakeHotWalletService : IHotWalletService
    {
        private HotWalletColdTransferOutcome _outcome =
            new HotWalletColdTransferOutcome.SidecarUnavailable("UNSET");

        public StablecoinType? LastToken { get; private set; }
        public Guid? LastAdminId { get; private set; }

        public void SetSuccess(
            long id, string txHash, decimal amount, StablecoinType token, string from, string to)
            => _outcome = new HotWalletColdTransferOutcome.Success(id, txHash, amount, token, from, to);

        public void SetInvalidAmount(string reason)
            => _outcome = new HotWalletColdTransferOutcome.InvalidAmount(reason);

        public void SetHotWalletNotConfigured()
            => _outcome = new HotWalletColdTransferOutcome.HotWalletNotConfigured();

        public void SetColdWalletNotConfigured()
            => _outcome = new HotWalletColdTransferOutcome.ColdWalletNotConfigured();

        public void SetSidecarUnavailable(string status)
            => _outcome = new HotWalletColdTransferOutcome.SidecarUnavailable(status);

        public Task<HotWalletColdTransferOutcome> InitiateColdTransferAsync(
            decimal amount, StablecoinType token, Guid initiatingAdminId, CancellationToken cancellationToken)
        {
            LastToken = token;
            LastAdminId = initiatingAdminId;
            return Task.FromResult(_outcome);
        }
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;
        private int _userSuffix;
        private const string SteamIdPrefix = "76561198555042";

        public FakeHotWalletService HotWalletFake { get; } = new();

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
                SteamDisplayName = $"WalletTester{suffix:D3}",
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

                // Stub the hot-wallet service at the HTTP boundary — the endpoint
                // test asserts auth/validation/mapping/envelope, not the on-chain
                // ledger write (HotWalletServiceTests owns that).
                services.RemoveAll<IHotWalletService>();
                services.AddSingleton<IHotWalletService>(_ => HotWalletFake);
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
