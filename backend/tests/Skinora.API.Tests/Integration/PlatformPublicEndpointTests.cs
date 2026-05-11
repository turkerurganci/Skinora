using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medallion.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Services;
using Skinora.API.Startup;
using Skinora.API.Tests.Common;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T63a — Integration coverage for the two public platform read endpoints
/// (07 §10.1 P1 stats, §10.2 P2 maintenance). Verifies the spec response
/// shape, anonymous access, and the per-endpoint <see cref="IMemoryCache"/>
/// behaviour: a second call within the TTL must serve the cached payload
/// rather than re-querying the database.
/// </summary>
public sealed class PlatformPublicEndpointTests : IClassFixture<PlatformPublicEndpointTests.Factory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public PlatformPublicEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ============================================================
    // P1 — GET /platform/stats
    // ============================================================

    [Fact]
    public async Task Stats_Anonymous_Returns200_WithUptimeAndZeroCount()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/platform/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");

        Assert.Equal(0, data.GetProperty("totalCompletedTransactions").GetInt32());
        Assert.Equal(99.9m, data.GetProperty("platformUptimePercent").GetDecimal());
    }

    [Fact]
    public async Task Stats_AfterCompletedTransactions_AggregatesCount()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.COMPLETED);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.COMPLETED);
        // Non-completed rows must be excluded from the counter.
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.CREATED);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.CANCELLED_BUYER);

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/platform/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal(2, data.GetProperty("totalCompletedTransactions").GetInt32());
    }

    [Fact]
    public async Task Stats_SecondCall_ServesCachedValue_NotFreshDbRead()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.COMPLETED);

        var client = _factory.CreateClient();
        var first = (await (await client.GetAsync("/api/v1/platform/stats"))
                .Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal(1, first.GetProperty("totalCompletedTransactions").GetInt32());

        // Add a second completed tx — the cached response must still report 1.
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.COMPLETED);

        var second = (await (await client.GetAsync("/api/v1/platform/stats"))
                .Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal(1, second.GetProperty("totalCompletedTransactions").GetInt32());
    }

    // ============================================================
    // P2 — GET /platform/maintenance
    // ============================================================

    [Fact]
    public async Task Maintenance_DefaultSeed_ReturnsInactive_WithNullFields()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/platform/maintenance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");

        Assert.False(data.GetProperty("active").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("type").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("message").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("plannedEnd").ValueKind);
    }

    [Fact]
    public async Task Maintenance_ActiveState_ReturnsTypeMessageAndPlannedEnd()
    {
        await _factory.SetMaintenanceAsync(
            active: true,
            type: "PLATFORM_MAINTENANCE",
            message: "Platform şu an bakımda. İşlem süreleri donduruldu.",
            plannedEnd: "2026-03-16T18:00:00Z");

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/platform/maintenance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");

        Assert.True(data.GetProperty("active").GetBoolean());
        Assert.Equal("PLATFORM_MAINTENANCE", data.GetProperty("type").GetString());
        Assert.Equal("Platform şu an bakımda. İşlem süreleri donduruldu.",
            data.GetProperty("message").GetString());
        Assert.Equal("2026-03-16T18:00:00Z", data.GetProperty("plannedEnd").GetString());
    }

    [Fact]
    public async Task Maintenance_SecondCall_ServesCachedValue_NotFreshDbRead()
    {
        var client = _factory.CreateClient();

        // Prime the cache with the default (inactive) state.
        var first = (await (await client.GetAsync("/api/v1/platform/maintenance"))
                .Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.False(first.GetProperty("active").GetBoolean());

        // Toggle to active in storage — the cached response must persist
        // until the 30 s TTL elapses.
        await _factory.SetMaintenanceAsync(
            active: true,
            type: "STEAM_OUTAGE",
            message: "Steam kesintisi",
            plannedEnd: "NONE");

        var second = (await (await client.GetAsync("/api/v1/platform/maintenance"))
                .Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.False(second.GetProperty("active").GetBoolean());
    }

    // ============================================================
    // Factory
    // ============================================================

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private const string TestSecret = "t63a-platform-test-secret-key-minimum-32-chars-padding!!";
        private readonly SqliteConnection _connection;
        private int _userSuffix;
        private const string SteamIdPrefix = "76561198777641";

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
                SteamDisplayName = $"T63aUser{suffix:D3}",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
            };
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public async Task<Transaction> CreateTransactionAsync(
            Guid sellerId,
            Guid buyerId,
            TransactionStatus status)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var buyerSteamId = await db.Set<User>().AsNoTracking()
                .Where(u => u.Id == buyerId)
                .Select(u => u.SteamId)
                .FirstAsync();

            var nowUtc = DateTime.UtcNow;
            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                Status = status,
                SellerId = sellerId,
                BuyerId = buyerId,
                TargetBuyerSteamId = buyerSteamId,
                BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
                BuyerRefundAddress = "TXBuyerRefund000000",
                ItemAssetId = "100200300",
                ItemClassId = "abc-class",
                ItemName = "AK-47 | Redline",
                ItemIconUrl = "https://steamcdn.example/img/test.png",
                StablecoinType = StablecoinType.USDT,
                Price = 100m,
                CommissionRate = 0.02m,
                CommissionAmount = 2m,
                TotalAmount = 102m,
                SellerPayoutAddress = "TXSellerPayout00000",
                PaymentTimeoutMinutes = 1440,
                CompletedAt = status == TransactionStatus.COMPLETED ? nowUtc.AddMinutes(-10) : null,
                CancelledAt = status == TransactionStatus.CANCELLED_BUYER ? nowUtc.AddMinutes(-5) : null,
                CancelledBy = status == TransactionStatus.CANCELLED_BUYER ? CancelledByType.BUYER : null,
                CancelReason = status == TransactionStatus.CANCELLED_BUYER ? "test cancel" : null,
            };
            db.Set<Transaction>().Add(tx);
            await db.SaveChangesAsync();
            return tx;
        }

        public async Task SetMaintenanceAsync(
            bool active,
            string type,
            string message,
            string plannedEnd)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await UpdateSettingAsync(db, "platform.maintenance.active", active ? "true" : "false");
            await UpdateSettingAsync(db, "platform.maintenance.type", type);
            await UpdateSettingAsync(db, "platform.maintenance.message", message);
            await UpdateSettingAsync(db, "platform.maintenance.planned_end", plannedEnd);
        }

        private static async Task UpdateSettingAsync(AppDbContext db, string key, string value)
        {
            var row = await db.Set<SystemSetting>().FirstAsync(s => s.Key == key);
            row.Value = value;
            row.IsConfigured = true;
            await db.SaveChangesAsync();
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Set<Transaction>().RemoveRange(
                db.Set<Transaction>().IgnoreQueryFilters().ToList());

            var seedIds = new[] { SeedConstants.SystemUserId };
            db.Set<User>().RemoveRange(
                db.Set<User>().IgnoreQueryFilters().Where(u => !seedIds.Contains(u.Id)));

            // Restore default maintenance seed (all inactive / NONE).
            ResetMaintenanceRow(db, "platform.maintenance.active", "false");
            ResetMaintenanceRow(db, "platform.maintenance.type", "NONE");
            ResetMaintenanceRow(db, "platform.maintenance.message", "NONE");
            ResetMaintenanceRow(db, "platform.maintenance.planned_end", "NONE");

            db.SaveChanges();

            // The IMemoryCache is registered as a singleton (default lifetime
            // for AddMemoryCache); clear both keys so each test sees a fresh
            // read path.
            var cache = Services.GetRequiredService<IMemoryCache>();
            cache.Remove(PlatformPublicService.StatsCacheKey);
            cache.Remove(PlatformPublicService.MaintenanceCacheKey);
        }

        private static void ResetMaintenanceRow(AppDbContext db, string key, string value)
        {
            var row = db.Set<SystemSetting>().First(s => s.Key == key);
            row.Value = value;
            row.IsConfigured = true;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(local);Database=SkinoraTest;Integrated Security=true;TrustServerCertificate=true");
            builder.UseSetting("Hangfire:DashboardEnabled", "false");

            // Auth wiring required by Program.cs even though the public
            // endpoints don't issue/consume tokens.
            builder.UseSetting("Jwt:Secret", TestSecret);
            builder.UseSetting("Jwt:Issuer", "skinora");
            builder.UseSetting("Jwt:Audience", "skinora-client");
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
}
