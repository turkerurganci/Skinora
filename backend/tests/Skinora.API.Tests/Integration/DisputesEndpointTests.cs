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
using Skinora.Disputes.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// HTTP-level smoke coverage for the T58 dispute endpoints
/// (07 §7.8–§7.10). Verifies wiring, auth gate, status code mapping and
/// envelope shape. Deeper service logic (auto-checker fan-out, outbox
/// emission) is verified by
/// <c>Skinora.Disputes.Tests/Integration/DisputeServiceTests</c>.
/// </summary>
public class DisputesEndpointTests : IClassFixture<DisputesEndpointTests.Factory>
{
    private const string TestSecret = "dispute-endpoint-test-secret-key-32!!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string ValidWallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public DisputesEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ---------- POST /transactions/:id/disputes ----------

    [Fact]
    public async Task Open_Unauthenticated_Returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{Guid.NewGuid():D}/disputes",
            new { type = "PAYMENT" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Open_HappyPath_Returns_200_With_OpenStatus_And_AutoCheck_Section()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.SELLER_CONFIRMED);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/disputes",
            new { type = "PAYMENT" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("PAYMENT", data.GetProperty("type").GetString());
        Assert.Equal("OPEN", data.GetProperty("status").GetString());
        var autoCheck = data.GetProperty("autoCheckResult");
        Assert.False(autoCheck.GetProperty("resolved").GetBoolean());
        Assert.True(autoCheck.GetProperty("canSubmitTxHash").GetBoolean());
        Assert.True(autoCheck.GetProperty("canEscalate").GetBoolean());

        // HasActiveDispute flipped on the transaction.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var refreshed = await db.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == transactionId);
        Assert.True(refreshed.HasActiveDispute);
    }

    [Fact]
    public async Task Open_NonBuyer_Returns_403_NotBuyer()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var stranger = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.SELLER_CONFIRMED);

        var client = BuildAuthenticatedClient(stranger.Id, stranger.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/disputes",
            new { type = "PAYMENT" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("NOT_BUYER",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Open_InvalidStateForType_Returns_409_InvalidStateTransition()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        // PAYMENT is openable only in SELLER_CONFIRMED / PAYMENT_RECEIVED per the
        // canonical DisputeEligibility matrix (02 §10.1).
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.ITEM_DELIVERED);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/disputes",
            new { type = "PAYMENT" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("INVALID_STATE_TRANSITION",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Open_DuplicateType_Returns_409_DuplicateDispute()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.SELLER_CONFIRMED);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var first = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/disputes",
            new { type = "PAYMENT" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/disputes",
            new { type = "PAYMENT" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("DUPLICATE_DISPUTE",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- POST /transactions/:id/disputes/:disputeId/escalate ----------

    [Fact]
    public async Task Escalate_HappyPath_Returns_200_And_Promotes_To_Escalated()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.SELLER_CONFIRMED);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var openResp = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/disputes",
            new { type = "PAYMENT" });
        var openBody = await openResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var disputeId = openBody.GetProperty("data").GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/disputes/{disputeId:D}/escalate",
            new { detail = "Ödemeyi gönderdim ama sistem hala görmüyor" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("ESCALATED", data.GetProperty("status").GetString());
        // WP17 — escalate response localized to the buyer's locale ("en" here).
        Assert.Equal("Your dispute has been forwarded to the admin team",
            data.GetProperty("message").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispute = await db.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == disputeId);
        Assert.Equal(DisputeStatus.ESCALATED, dispute.Status);
        Assert.NotNull(dispute.UserDescription);
    }

    [Fact]
    public async Task Escalate_DetailTooShort_Returns_400_ValidationError()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.SELLER_CONFIRMED);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var openResp = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/disputes",
            new { type = "PAYMENT" });
        var openBody = await openResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var disputeId = openBody.GetProperty("data").GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/disputes/{disputeId:D}/escalate",
            new { detail = "kısa" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("VALIDATION_ERROR",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- POST /transactions/:id/disputes/:disputeId/submit-txhash ----------

    [Fact]
    public async Task SubmitTxHash_NonPaymentDispute_Returns_422_NotPaymentDispute()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.PAYMENT_RECEIVED);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var openResp = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/disputes",
            new { type = "DELIVERY" });
        Assert.Equal(HttpStatusCode.OK, openResp.StatusCode);
        var openBody = await openResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var disputeId = openBody.GetProperty("data").GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/disputes/{disputeId:D}/submit-txhash",
            new { txHash = "0123456789abcdef0123" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("NOT_PAYMENT_DISPUTE",
            body.GetProperty("error").GetProperty("code").GetString());
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
            string steamId64, string itemAssetId, CancellationToken cancellationToken)
            => Task.FromResult(InventoryLookupResult.NotFound);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private const string SteamIdBase = "76561198777999";
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
                ItemAssetId = Guid.NewGuid().ToString("N")[..12],
                ItemClassId = "abc-class",
                ItemName = "AK-47 | Redline",
                StablecoinType = StablecoinType.USDT,
                Price = 100m,
                CommissionRate = 0.02m,
                CommissionAmount = 2m,
                TotalAmount = 102m,
                SellerPayoutAddress = ValidWallet,
                PaymentTimeoutMinutes = 1440,
                AcceptedAt = DateTime.UtcNow.AddMinutes(-10),
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
            db.Set<Dispute>().RemoveRange(db.Set<Dispute>());
            db.Set<Transaction>().RemoveRange(db.Set<Transaction>());
            var seedIds = new[] { Skinora.Shared.Domain.Seed.SeedConstants.SystemUserId };
            db.Set<User>().RemoveRange(db.Set<User>().Where(u => !seedIds.Contains(u.Id)));
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

                services.RemoveAll<IRefreshTokenCache>();
                services.AddSingleton<IRefreshTokenCache, NullRefreshTokenCache>();

                services.RemoveAll<ISteamInventoryReader>();
                services.AddSingleton<ISteamInventoryReader, StubInventoryReader>();
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
