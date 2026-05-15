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
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Persistence;
using Skinora.Steam.Application.Inventory;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T67 — Integration coverage for S1 <c>GET /steam/inventory</c> (07 §6.1).
/// Verifies authentication, the three sidecar outcomes (success / private /
/// upstream failure), and the 5/min rate-limit ceiling.
/// </summary>
public sealed class SteamInventoryEndpointTests : IClassFixture<SteamInventoryEndpointTests.Factory>
{
    private const string TestSecret = "steam-inventory-test-secret-key-32-chars!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public SteamInventoryEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task GetInventory_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/steam/inventory");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetInventory_Authenticated_Success_Returns200_WithEnvelope()
    {
        var user = await _factory.CreateUserAsync();
        _factory.InventoryFake.SetSuccess(new SteamInventoryDto(
            Items: new[]
            {
                new SteamInventoryItemDto(
                    AssetId: "27348562891",
                    ClassId: "310776959",
                    InstanceId: "188530139",
                    Name: "AK-47 | Redline",
                    MarketHashName: "AK-47 | Redline (Field-Tested)",
                    Type: "Rifle",
                    Wear: "Field-Tested",
                    ImageUrl: "https://cdn.test/ak.png",
                    Tradeable: true,
                    Marketable: true),
            },
            TotalCount: 1,
            TradeableCount: 1));

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.GetAsync("/api/v1/steam/inventory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // 07 §2.4 — Success responses are wrapped in ApiResponse<T> { data: ... }.
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, data.GetProperty("tradeableCount").GetInt32());
        var first = data.GetProperty("items")[0];
        Assert.Equal("27348562891", first.GetProperty("assetId").GetString());
        Assert.Equal("AK-47 | Redline", first.GetProperty("name").GetString());
        Assert.Equal("Field-Tested", first.GetProperty("wear").GetString());
        Assert.True(first.GetProperty("tradeable").GetBoolean());

        Assert.Equal(user.SteamId, _factory.InventoryFake.LastSteamId);
    }

    [Fact]
    public async Task GetInventory_Private_Returns422_InventoryPrivate()
    {
        var user = await _factory.CreateUserAsync();
        _factory.InventoryFake.SetPrivate();

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.GetAsync("/api/v1/steam/inventory");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("INVENTORY_PRIVATE", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetInventory_Unavailable_Returns503_SteamUnavailable()
    {
        var user = await _factory.CreateUserAsync();
        _factory.InventoryFake.SetUnavailable();

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.GetAsync("/api/v1/steam/inventory");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("STEAM_UNAVAILABLE", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetInventory_Authenticated_PassesSteamIdClaim_ToQueryService()
    {
        // Two distinct users; each call should forward the caller's own
        // SteamID claim (no cross-user lookup).
        var alice = await _factory.CreateUserAsync(u => u.SteamId = "76561198000000111");
        var bob = await _factory.CreateUserAsync(u => u.SteamId = "76561198000000222");
        _factory.InventoryFake.SetSuccess(new SteamInventoryDto(
            Items: Array.Empty<SteamInventoryItemDto>(), TotalCount: 0, TradeableCount: 0));

        var aliceClient = BuildAuthenticatedClient(alice.Id, alice.SteamId);
        await aliceClient.GetAsync("/api/v1/steam/inventory");
        Assert.Equal(alice.SteamId, _factory.InventoryFake.LastSteamId);

        var bobClient = BuildAuthenticatedClient(bob.Id, bob.SteamId);
        await bobClient.GetAsync("/api/v1/steam/inventory");
        Assert.Equal(bob.SteamId, _factory.InventoryFake.LastSteamId);
    }

    [Fact]
    public async Task GetInventory_RateLimit_5PerMinute_SixthCallReturns429()
    {
        // 07 §6.1 / RateLimit policy "steam-inventory" = 5 requests / 60s.
        // We hit the endpoint six times back-to-back with a Success response
        // each time; only the sixth must be throttled.
        var user = await _factory.CreateUserAsync();
        _factory.InventoryFake.SetSuccess(new SteamInventoryDto(
            Items: Array.Empty<SteamInventoryItemDto>(), TotalCount: 0, TradeableCount: 0));

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        for (var i = 0; i < 5; i++)
        {
            var ok = await client.GetAsync("/api/v1/steam/inventory");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var throttled = await client.GetAsync("/api/v1/steam/inventory");
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
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

    /// <summary>
    /// Fake <see cref="ISteamInventoryQueryService"/>. Recorded for assertion
    /// + scriptable — three setters configure the three discriminated results
    /// the controller has to handle.
    /// </summary>
    public sealed class FakeSteamInventoryQueryService : ISteamInventoryQueryService
    {
        private GetInventoryResult _result =
            new(GetInventoryStatus.SteamUnavailable, Inventory: null);

        public string? LastSteamId { get; private set; }

        public void SetSuccess(SteamInventoryDto inventory)
            => _result = new GetInventoryResult(GetInventoryStatus.Success, inventory);

        public void SetPrivate()
            => _result = new GetInventoryResult(GetInventoryStatus.InventoryPrivate, Inventory: null);

        public void SetUnavailable()
            => _result = new GetInventoryResult(GetInventoryStatus.SteamUnavailable, Inventory: null);

        public Task<GetInventoryResult> GetForSteamIdAsync(
            string steamId, CancellationToken cancellationToken)
        {
            LastSteamId = steamId;
            return Task.FromResult(_result);
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

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;

        public FakeSteamInventoryQueryService InventoryFake { get; } = new();

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public async Task<User> CreateUserAsync(Action<User>? customize = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = $"76561198{Random.Shared.NextInt64(100_000_000, 999_999_999)}",
                SteamDisplayName = "Tester",
                PreferredLanguage = "en",
            };
            customize?.Invoke(user);
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public void Reset()
        {
            InventoryFake.SetUnavailable();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<User>().RemoveRange(db.Set<User>());
            db.SaveChanges();

            // Rate-limit counters live in the in-memory store; clear between tests
            // so the 5-per-minute ceiling resets cleanly.
            var rateStore = scope.ServiceProvider.GetService<IRateLimiterStore>();
            (rateStore as InMemoryRateLimiterStore)?.Reset();
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

            // SteamSidecar — base URL irrelevant because the query service is
            // swapped for the fake below, but the options binding must not
            // throw on startup.
            builder.UseSetting("SteamSidecar:BaseUrl", "http://localhost:65500");
            builder.UseSetting("SteamSidecar:InternalKey", "test-internal-key");

            builder.ConfigureServices(services =>
            {
                // EF Core — strip SqlServer stack and re-add SQLite (mirrors
                // AuthSessionEndpointTests.Factory).
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

                // Hangfire — bypass for test runs.
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

                // T67 — swap the production query service for the fake under
                // test control.
                services.RemoveAll<ISteamInventoryQueryService>();
                services.AddSingleton<ISteamInventoryQueryService>(_ => InventoryFake);
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
