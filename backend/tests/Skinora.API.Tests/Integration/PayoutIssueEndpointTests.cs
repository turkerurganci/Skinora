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
using Skinora.Auth.Application.Session;
using Skinora.Auth.Configuration;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;
using Skinora.Transactions.Application.PayoutIssues;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// HTTP-level smoke coverage for T60
/// <c>POST /transactions/:id/report-payout-issue</c> (07 §7.11). Verifies
/// route wiring, auth gate, status code mapping, and envelope shape. Deeper
/// service logic (verifier outcomes → state transitions, outbox emission)
/// lives in <c>Skinora.Transactions.Tests/Integration/PayoutIssues/PayoutIssueServiceTests</c>
/// where it runs against a real SQL Server.
/// </summary>
/// <remarks>
/// SQLite in-memory cannot enforce the SellerPayoutIssue filtered unique
/// index (06 §3.8a) — it is documented but defensive pre-checks in
/// <c>PayoutIssueService</c> still surface ISSUE_ALREADY_REPORTED. The
/// in-memory verifier override returns deterministic outcomes per scenario.
/// </remarks>
public class PayoutIssueEndpointTests : IClassFixture<PayoutIssueEndpointTests.Factory>
{
    private const string TestSecret = "payout-issue-endpoint-test-secret-key-32!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string ValidWallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";
    private const string ValidDetail = "Ödeme cüzdanıma ulaşmadı, kontrol istiyorum.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public PayoutIssueEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task Report_Unauthenticated_Returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{Guid.NewGuid():D}/report-payout-issue",
            new { detail = ValidDetail });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Report_HappyPath_StubVerifier_Returns_201_And_Escalated()
    {
        var admin = await _factory.CreateUserAsync();
        _factory.AdminId = admin.Id;
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.COMPLETED);

        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/report-payout-issue",
            new { detail = ValidDetail });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.NotEqual(Guid.Empty, data.GetProperty("issueId").GetGuid());
        // StubPayoutVerifier always returns UnableToVerify → service escalates.
        Assert.Equal("ESCALATED", data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Report_NonSeller_Returns_403_NotSeller()
    {
        var admin = await _factory.CreateUserAsync();
        _factory.AdminId = admin.Id;
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var stranger = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.COMPLETED);

        var client = BuildAuthenticatedClient(stranger.Id, stranger.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/report-payout-issue",
            new { detail = ValidDetail });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("NOT_SELLER",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Report_BuyerCallsAsSeller_Returns_403_NotSeller()
    {
        var admin = await _factory.CreateUserAsync();
        _factory.AdminId = admin.Id;
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.COMPLETED);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/report-payout-issue",
            new { detail = ValidDetail });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Report_TransactionNotCompleted_Returns_409_TransactionNotCompleted()
    {
        var admin = await _factory.CreateUserAsync();
        _factory.AdminId = admin.Id;
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.ITEM_DELIVERED);

        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/report-payout-issue",
            new { detail = ValidDetail });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("TRANSACTION_NOT_COMPLETED",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Report_TransactionNotFound_Returns_404_TransactionNotFound()
    {
        var admin = await _factory.CreateUserAsync();
        _factory.AdminId = admin.Id;
        var seller = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{Guid.NewGuid():D}/report-payout-issue",
            new { detail = ValidDetail });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("TRANSACTION_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Report_DuplicateActiveIssue_Returns_409_IssueAlreadyReported()
    {
        var admin = await _factory.CreateUserAsync();
        _factory.AdminId = admin.Id;
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.COMPLETED);

        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);
        var first = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/report-payout-issue",
            new { detail = ValidDetail });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/report-payout-issue",
            new { detail = ValidDetail });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("ISSUE_ALREADY_REPORTED",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Report_DetailTooShort_Returns_400_ValidationError()
    {
        var admin = await _factory.CreateUserAsync();
        _factory.AdminId = admin.Id;
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.COMPLETED);

        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/report-payout-issue",
            new { detail = "kısa" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("VALIDATION_ERROR",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Report_EmptyBody_Returns_400_ValidationError()
    {
        var admin = await _factory.CreateUserAsync();
        _factory.AdminId = admin.Id;
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.COMPLETED);

        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);
        var response = await client.PostAsync(
            $"/api/v1/transactions/{transactionId:D}/report-payout-issue",
            new StringContent(string.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    /// <summary>
    /// These suites never assert on inventory contents, so the double answers
    /// "inventory readable, asset absent" (T121) — the branch the pre-T121
    /// <c>null</c> stood for here, kept explicit so a later reader does not
    /// mistake it for a simulated Steam outage.
    /// </summary>
    private sealed class StubInventoryReader : ISteamInventoryReader
    {
        public Task<InventoryLookupResult> GetItemAsync(
            string steamId64, string itemAssetId,
            InventoryReadFreshness freshness, CancellationToken cancellationToken)
            => Task.FromResult(InventoryLookupResult.NotFound);

        /// <summary>T123 — unused here; "unknown", never a fabricated baseline.</summary>
        public Task<InventoryClassBaselineResult> CaptureClassBaselineAsync(
            string steamId64, string classId, string? instanceId,
            InventoryReadFreshness freshness, CancellationToken cancellationToken)
            => Task.FromResult(InventoryClassBaselineResult.Unavailable);

        /// <summary>T130 — same reasoning; an empty fingerprint would be a claim.</summary>
        public Task<InventoryFingerprintResult> CaptureInventoryFingerprintAsync(
            string steamId64, InventoryReadFreshness freshness,
            CancellationToken cancellationToken)
            => Task.FromResult(InventoryFingerprintResult.Unavailable);
    }

    private sealed class TestAdminResolver : IPayoutEscalationAdminResolver
    {
        private readonly Factory _factory;
        public TestAdminResolver(Factory factory) => _factory = factory;

        public Task<Guid?> ResolveAdminUserIdAsync(CancellationToken cancellationToken)
            => Task.FromResult(_factory.AdminId);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private const string SteamIdBase = "76561198888888";
        private readonly SqliteConnection _connection;
        private int _userSuffix;

        public Guid? AdminId { get; set; }

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
                SteamId = $"{SteamIdBase}{suffix:D3}",
                SteamDisplayName = "Tester",
                PreferredLanguage = "en",
                MobileAuthenticatorVerified = true,
                CreatedAt = DateTime.UtcNow.AddDays(-200),
            };
            customize?.Invoke(user);
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public async Task<Guid> SeedTransactionAsync(
            Guid sellerId,
            Guid buyerId,
            TransactionStatus status)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                Status = status,
                SellerId = sellerId,
                BuyerId = buyerId,
                BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
                TargetBuyerSteamId = "76561198000099900",
                ItemAssetId = "27348562891",
                ItemClassId = "abc-class",
                ItemName = "AK-47 | Redline",
                StablecoinType = StablecoinType.USDT,
                Price = 100m,
                CommissionRate = 0.02m,
                CommissionAmount = 2m,
                TotalAmount = 102m,
                SellerPayoutAddress = ValidWallet,
                PaymentTimeoutMinutes = 1440,
                AcceptedAt = DateTime.UtcNow.AddMinutes(-30),
            };
            db.Set<Transaction>().Add(transaction);
            await db.SaveChangesAsync();
            return transaction.Id;
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<OutboxMessage>().RemoveRange(db.Set<OutboxMessage>());
            db.Set<SellerPayoutIssue>().RemoveRange(db.Set<SellerPayoutIssue>());
            db.Set<Transaction>().RemoveRange(db.Set<Transaction>());
            var seedIds = new[] { Skinora.Shared.Domain.Seed.SeedConstants.SystemUserId };
            db.Set<User>().RemoveRange(db.Set<User>().Where(u => !seedIds.Contains(u.Id)));
            db.SaveChanges();
            AdminId = null;
            _userSuffix = 0;
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

                services.RemoveAll<IRefreshTokenCache>();
                services.AddSingleton<IRefreshTokenCache, NullRefreshTokenCache>();

                services.RemoveAll<ISteamInventoryReader>();
                services.AddSingleton<ISteamInventoryReader, StubInventoryReader>();

                // T60 — override the AdminUserRole-backed resolver because the
                // SQLite test bed does not seed admin role assignments.
                services.RemoveAll<IPayoutEscalationAdminResolver>();
                services.AddSingleton<IPayoutEscalationAdminResolver>(_ => new TestAdminResolver(this));
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
