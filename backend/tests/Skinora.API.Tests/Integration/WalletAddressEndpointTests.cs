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
using Skinora.Auth.Application.ReAuthentication;
using Skinora.Auth.Application.Session;
using Skinora.Auth.Configuration;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Application.Wallet;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

public class WalletAddressEndpointTests : IClassFixture<WalletAddressEndpointTests.Factory>
{
    private const string TestSecret = "wallet-test-secret-key-minimum-32-chars!!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string SteamIdPrefix = "76561198333444";
    // All 34-char, T-prefixed, Base58-only (Base58 alphabet excludes 0, O, I, l).
    private const string ValidSellerAddress = "TAaBbCcDdEeFfGgHhJjKkMmNnPp1234567";
    private const string ValidSecondSellerAddress = "TQqRrSsTtUuVvWwXxYyZzabcdef1234567";
    private const string ValidRefundAddress = "TghijkmnopqrstuvwxyzABCDEFGHJ12345";
    private const string UnrelatedSellerAddress = "TKLMNPQRSTUVWXYZabcdefghijk1234567";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public WalletAddressEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ---------- format validation ----------

    [Fact]
    public async Task UpdateSellerWallet_InvalidFormat_Returns400InvalidWalletAddress()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        // Missing 'T' prefix → INVALID_WALLET_ADDRESS.
        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = "XYz1234567890abcdef1234567890abcde",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("INVALID_WALLET_ADDRESS", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateSellerWallet_WrongLength_Returns400InvalidWalletAddress()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = "TShort",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("INVALID_WALLET_ADDRESS", body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- first-time save (no existing address) ----------

    [Fact]
    public async Task UpdateSellerWallet_NoExistingAddress_ReturnsSuccessAndPersists()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSellerAddress,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(ValidSellerAddress, data.GetProperty("walletAddress").GetString());
        Assert.Equal(0, data.GetProperty("activeTransactionsUsingOldAddress").GetInt32());

        // Persistence: DefaultPayoutAddress + PayoutAddressChangedAt both populated.
        var persisted = await _factory.GetUserAsync(user.Id);
        Assert.Equal(ValidSellerAddress, persisted.DefaultPayoutAddress);
        Assert.NotNull(persisted.PayoutAddressChangedAt);
        Assert.Null(persisted.DefaultRefundAddress);
        Assert.Null(persisted.RefundAddressChangedAt);
    }

    // ---------- re-auth enforcement (existing address) ----------

    [Fact]
    public async Task UpdateSellerWallet_ExistingAddress_WithoutReAuthToken_Returns403ReAuthRequired()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.DefaultPayoutAddress = ValidSellerAddress;
        });
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSecondSellerAddress,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("RE_AUTH_REQUIRED", body.GetProperty("error").GetProperty("code").GetString());

        // No mutation on failure path.
        var persisted = await _factory.GetUserAsync(user.Id);
        Assert.Equal(ValidSellerAddress, persisted.DefaultPayoutAddress);
    }

    [Fact]
    public async Task UpdateSellerWallet_ExistingAddress_WithInvalidReAuthToken_Returns403TokenInvalid()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.DefaultPayoutAddress = ValidSellerAddress;
        });
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add("X-ReAuth-Token", "garbage-token");

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSecondSellerAddress,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("RE_AUTH_TOKEN_INVALID", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateSellerWallet_ExistingAddress_WithValidReAuthToken_Persists()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.DefaultPayoutAddress = ValidSellerAddress;
        });
        var token = await _factory.IssueReAuthTokenAsync(user.Id, user.SteamId);
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add("X-ReAuth-Token", token);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSecondSellerAddress,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Token is single-use — the next attempt with the same token fails.
        var reuse = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSellerAddress,
        });
        Assert.Equal(HttpStatusCode.Forbidden, reuse.StatusCode);
        var body = await reuse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("RE_AUTH_TOKEN_INVALID", body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- sanctions ----------

    [Fact]
    public async Task UpdateSellerWallet_SanctionsMatch_Returns403AndLeavesAddressUnchanged()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        _factory.SanctionsStub.ShouldMatch = true;

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSellerAddress,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("SANCTIONS_MATCH", body.GetProperty("error").GetProperty("code").GetString());

        var persisted = await _factory.GetUserAsync(user.Id);
        Assert.Null(persisted.DefaultPayoutAddress);
        Assert.Null(persisted.PayoutAddressChangedAt);
    }

    // ---------- activeTransactionsUsingOldAddress count ----------

    [Fact]
    public async Task UpdateSellerWallet_CountsNonTerminalTransactionsOnly()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.DefaultPayoutAddress = ValidSellerAddress;
        });

        // Two non-terminal (CREATED, PAYMENT_RECEIVED) + one terminal (COMPLETED)
        // using ValidSellerAddress as seller payout snapshot. One more
        // transaction uses a different address and must not be counted.
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.SellerPayoutAddress = ValidSellerAddress;
            t.Status = TransactionStatus.CREATED;
        });
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.SellerPayoutAddress = ValidSellerAddress;
            t.Status = TransactionStatus.PAYMENT_RECEIVED;
        });
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.SellerPayoutAddress = ValidSellerAddress;
            t.Status = TransactionStatus.COMPLETED;
        });
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.SellerPayoutAddress = UnrelatedSellerAddress;
            t.Status = TransactionStatus.CREATED;
        });

        var token = await _factory.IssueReAuthTokenAsync(user.Id, user.SteamId);
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add("X-ReAuth-Token", token);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSecondSellerAddress,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(2, data.GetProperty("activeTransactionsUsingOldAddress").GetInt32());
    }

    // ---------- refund endpoint ----------

    [Fact]
    public async Task UpdateRefundWallet_NoExistingAddress_PersistsAndSetsTimestamp()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/refund", new
        {
            walletAddress = ValidRefundAddress,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(ValidRefundAddress, data.GetProperty("walletAddress").GetString());

        var persisted = await _factory.GetUserAsync(user.Id);
        Assert.Equal(ValidRefundAddress, persisted.DefaultRefundAddress);
        Assert.NotNull(persisted.RefundAddressChangedAt);
        // Seller fields untouched.
        Assert.Null(persisted.DefaultPayoutAddress);
        Assert.Null(persisted.PayoutAddressChangedAt);
    }

    // ---------- auth ----------

    [Fact]
    public async Task UpdateSellerWallet_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSellerAddress,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    public sealed class ConfigurableSanctionsStub : IWalletSanctionsCheck
    {
        public bool ShouldMatch { get; set; }
        public string MatchedList { get; set; } = "OFAC";

        public Task<WalletSanctionsDecision> EvaluateAsync(
            string walletAddress, CancellationToken cancellationToken)
            => Task.FromResult(ShouldMatch
                ? WalletSanctionsDecision.Match(MatchedList)
                : WalletSanctionsDecision.NoMatch());
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;
        private int _userSuffix;
        private int _transactionSuffix;

        public ConfigurableSanctionsStub SanctionsStub { get; } = new();

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
                SteamDisplayName = "WalletTester",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
            };
            customize?.Invoke(user);

            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public async Task<Transaction> CreateTransactionAsync(Action<Transaction>? customize = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Interlocked.Increment(ref _transactionSuffix);
            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                Status = TransactionStatus.CREATED,
                SellerId = Guid.NewGuid(),
                // Pair STEAM_ID method with TargetBuyerSteamId (InviteToken NULL)
                // to satisfy CK_Transactions_BuyerMethod_SteamId.
                BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
                TargetBuyerSteamId = $"765611999{suffix:D8}",
                ItemAssetId = $"asset-{suffix}",
                ItemClassId = $"class-{suffix}",
                ItemName = $"Item {suffix}",
                StablecoinType = StablecoinType.USDT,
                Price = 10m,
                CommissionRate = 0.02m,
                CommissionAmount = 0.2m,
                TotalAmount = 10.2m,
                // 34-char base58, T-prefix; transaction snapshot placeholder.
                SellerPayoutAddress = "TDefau1t4567abcdefghijkmnopqrs1234",
                PaymentTimeoutMinutes = 60,
                CreatedAt = DateTime.UtcNow,
            };
            customize?.Invoke(tx);

            db.Set<Transaction>().Add(tx);
            await db.SaveChangesAsync();
            return tx;
        }

        public async Task<User> GetUserAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<User>().AsNoTracking().FirstAsync(u => u.Id == userId);
        }

        public async Task<string> IssueReAuthTokenAsync(Guid userId, string steamId)
        {
            using var scope = Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IReAuthTokenStore>();
            var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            await store.IssueAsync(
                token,
                new ReAuthTokenPayload(userId, steamId),
                TimeSpan.FromMinutes(5),
                CancellationToken.None);
            return token;
        }

        public void Reset()
        {
            SanctionsStub.ShouldMatch = false;

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

                // T34 — in-memory re-auth store (no Redis) + per-test sanctions stub.
                services.RemoveAll<IReAuthTokenStore>();
                services.AddSingleton<IReAuthTokenStore>(sp =>
                    new InMemoryReAuthTokenStore(sp.GetRequiredService<TimeProvider>()));

                services.RemoveAll<IWalletSanctionsCheck>();
                services.AddSingleton<IWalletSanctionsCheck>(SanctionsStub);
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
