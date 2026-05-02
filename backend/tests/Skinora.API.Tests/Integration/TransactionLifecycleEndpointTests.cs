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
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;
using Skinora.Transactions.Application.Steam;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// HTTP-level smoke coverage for the T45 transaction lifecycle endpoints
/// (07 §7.2–§7.4): wiring, auth gate, rate-limit policy, response envelope.
/// Deeper service logic is verified by
/// <c>Skinora.Transactions.Tests/Integration/Lifecycle/*</c>.
/// </summary>
public class TransactionLifecycleEndpointTests : IClassFixture<TransactionLifecycleEndpointTests.Factory>
{
    private const string TestSecret = "tx-lifecycle-test-secret-key-32chars!!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string SteamId = "76561198777999001";
    private const string ValidWallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public TransactionLifecycleEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task Eligibility_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/transactions/eligibility");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Eligibility_Authenticated_ReturnsDto()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = true;
            u.DefaultPayoutAddress = ValidWallet;
        });
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.GetAsync("/api/v1/transactions/eligibility");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.True(data.GetProperty("eligible").GetBoolean());
        Assert.True(data.GetProperty("mobileAuthenticatorActive").GetBoolean());
    }

    [Fact]
    public async Task Params_Authenticated_ReturnsDto()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.GetAsync("/api/v1/transactions/params");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.True(data.TryGetProperty("minPrice", out _));
        Assert.True(data.TryGetProperty("maxPrice", out _));
        Assert.True(data.TryGetProperty("commissionRate", out _));
        Assert.True(data.TryGetProperty("paymentTimeout", out _));
        Assert.True(data.TryGetProperty("supportedStablecoins", out _));
    }

    [Fact]
    public async Task Create_Happy_Path_Returns_201_And_Persists_Transaction()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = true;
            u.DefaultPayoutAddress = ValidWallet;
        });
        _factory.SeedInventoryItem(user.SteamId, "27348562891", "AK-47 | Redline");

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        var request = new
        {
            itemAssetId = "27348562891",
            stablecoin = "USDT",
            price = "100.00",
            paymentTimeoutHours = 24,
            buyerIdentificationMethod = "STEAM_ID",
            buyerSteamId = "76561198000000999",
            sellerWalletAddress = ValidWallet,
        };

        var response = await client.PostAsJsonAsync("/api/v1/transactions", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("CREATED", data.GetProperty("status").GetString());

        // Verify the OutboxMessage row was written in the same SaveChanges
        // (T45 acceptance criterion: TransactionCreatedEvent emitted via outbox).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(db.Set<OutboxMessage>().AsNoTracking().ToList());
    }

    [Fact]
    public async Task Create_Below_Minimum_Price_Returns_422()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = true;
            u.DefaultPayoutAddress = ValidWallet;
        });
        _factory.SeedInventoryItem(user.SteamId, "27348562891", "AK-47 | Redline");

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        var request = new
        {
            itemAssetId = "27348562891",
            stablecoin = "USDT",
            price = "1.00",
            paymentTimeoutHours = 24,
            buyerIdentificationMethod = "STEAM_ID",
            buyerSteamId = "76561198000000999",
            sellerWalletAddress = ValidWallet,
        };

        // Force the configured minimum to be larger than the request price.
        await _factory.ConfigureSettingAsync("min_transaction_amount", "10");

        var response = await client.PostAsJsonAsync("/api/v1/transactions", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("PRICE_OUT_OF_RANGE",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Detail_Anonymous_Returns_Public_Variant()
    {
        var seller = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id);

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/transactions/{transactionId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        // Public variant contract: userRole absent (suppressed via WhenWritingNull),
        // commission/total absent, availableActions has requiresLogin=true.
        Assert.False(data.TryGetProperty("commissionAmount", out _));
        Assert.False(data.TryGetProperty("totalAmount", out _));
        var actions = data.GetProperty("availableActions");
        Assert.False(actions.GetProperty("canAccept").GetBoolean());
        Assert.True(actions.GetProperty("requiresLogin").GetBoolean());
    }

    [Fact]
    public async Task Detail_Authenticated_Buyer_Returns_Full_Variant()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        // Steam ID match → service resolves the target buyer as
        // role="buyer" even before BuyerId is set (03 §3.2 step 1).
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: buyer.SteamId);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.GetAsync($"/api/v1/transactions/{transactionId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("buyer", data.GetProperty("userRole").GetString());
        Assert.Equal("102.00", data.GetProperty("totalAmount").GetString());
    }

    [Fact]
    public async Task Detail_Non_Party_Returns_403()
    {
        var seller = await _factory.CreateUserAsync();
        var stranger = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id);

        var client = BuildAuthenticatedClient(stranger.Id, stranger.SteamId);
        var response = await client.GetAsync($"/api/v1/transactions/{transactionId:D}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("NOT_A_PARTY",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Detail_Not_Found_Returns_404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/transactions/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("TRANSACTION_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Accept_Unauthenticated_Returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{Guid.NewGuid():D}/accept",
            new { refundWalletAddress = ValidWallet });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Accept_Happy_Path_Transitions_To_Accepted_And_Emits_Outbox()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: buyer.SteamId);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/accept",
            new { refundWalletAddress = ValidWallet });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("ACCEPTED", data.GetProperty("status").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(db.Set<OutboxMessage>().AsNoTracking().ToList());
    }

    [Fact]
    public async Task Accept_Steam_Id_Mismatch_Returns_403()
    {
        var seller = await _factory.CreateUserAsync();
        var stranger = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: "76561198000099999");

        var client = BuildAuthenticatedClient(stranger.Id, stranger.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/accept",
            new { refundWalletAddress = ValidWallet });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("STEAM_ID_MISMATCH",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Accept_Invalid_Wallet_Returns_400()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: buyer.SteamId);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/accept",
            new { refundWalletAddress = "NOT_A_TRC20_ADDRESS" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("INVALID_WALLET_ADDRESS",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_Eligibility_Fail_Returns_422()
    {
        // No MA → eligibility fails with MOBILE_AUTHENTICATOR_REQUIRED.
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = false;
            u.DefaultPayoutAddress = ValidWallet;
        });

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        var request = new
        {
            itemAssetId = "27348562891",
            stablecoin = "USDT",
            price = "100.00",
            paymentTimeoutHours = 24,
            buyerIdentificationMethod = "STEAM_ID",
            buyerSteamId = "76561198000000999",
            sellerWalletAddress = ValidWallet,
        };

        var response = await client.PostAsJsonAsync("/api/v1/transactions", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("MOBILE_AUTHENTICATOR_REQUIRED",
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
    /// In-process replacement for <see cref="ISteamInventoryReader"/>. Tests
    /// register tradeable items via <see cref="Register"/>; everything else
    /// returns <c>null</c> so the controller correctly maps to
    /// <c>ITEM_NOT_IN_INVENTORY</c>.
    /// </summary>
    private sealed class FakeSteamInventoryReader : ISteamInventoryReader
    {
        private readonly Dictionary<(string steamId, string assetId), InventoryItemSnapshot> _items = [];

        public void Register(string steamId, string assetId, string name)
            => _items[(steamId, assetId)] = new InventoryItemSnapshot(
                AssetId: assetId,
                ClassId: "test-class",
                InstanceId: "test-instance",
                Name: name,
                IconUrl: null,
                Exterior: null,
                Type: null,
                InspectLink: null,
                IsTradeable: true);

        public Task<InventoryItemSnapshot?> TryGetItemAsync(
            string steamId64, string itemAssetId, CancellationToken cancellationToken)
            => Task.FromResult(_items.TryGetValue((steamId64, itemAssetId), out var item) ? item : null);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;
        private readonly FakeSteamInventoryReader _inventory = new();
        private int _userSuffix;

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public void SeedInventoryItem(string steamId, string assetId, string name)
            => _inventory.Register(steamId, assetId, name);

        public async Task ConfigureSettingAsync(string key, string value)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existing = db.Set<Skinora.Platform.Domain.Entities.SystemSetting>()
                .FirstOrDefault(s => s.Key == key);
            if (existing is null)
            {
                db.Set<Skinora.Platform.Domain.Entities.SystemSetting>().Add(
                    new Skinora.Platform.Domain.Entities.SystemSetting
                    {
                        Id = Guid.NewGuid(),
                        Key = key,
                        Value = value,
                        IsConfigured = true,
                        DataType = "string",
                        Category = "Test",
                    });
            }
            else
            {
                existing.Value = value;
                existing.IsConfigured = true;
            }
            await db.SaveChangesAsync();
        }

        public async Task<Guid> SeedTransactionAsync(
            Guid sellerId,
            string? targetBuyerSteamId = null,
            Skinora.Shared.Enums.TransactionStatus status = Skinora.Shared.Enums.TransactionStatus.CREATED)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var nowUtc = DateTime.UtcNow;
            var transaction = new Skinora.Transactions.Domain.Entities.Transaction
            {
                Id = Guid.NewGuid(),
                Status = status,
                SellerId = sellerId,
                BuyerIdentificationMethod = Skinora.Shared.Enums.BuyerIdentificationMethod.STEAM_ID,
                // CK_Transactions_BuyerMethod_SteamId: STEAM_ID method requires
                // TargetBuyerSteamId NOT NULL. Tests that don't care about a
                // specific buyer pass an arbitrary non-matching ID.
                TargetBuyerSteamId = targetBuyerSteamId ?? "76561198999999999",
                ItemAssetId = "27348562891",
                ItemClassId = "abc-class",
                ItemName = "AK-47 | Redline",
                StablecoinType = Skinora.Shared.Enums.StablecoinType.USDT,
                Price = 100m,
                CommissionRate = 0.02m,
                CommissionAmount = 2m,
                TotalAmount = 102m,
                SellerPayoutAddress = ValidWallet,
                PaymentTimeoutMinutes = 1440,
                AcceptDeadline = status == Skinora.Shared.Enums.TransactionStatus.CREATED
                    ? nowUtc.AddHours(1)
                    : null,
            };
            db.Set<Skinora.Transactions.Domain.Entities.Transaction>().Add(transaction);
            await db.SaveChangesAsync();
            return transaction.Id;
        }

        public async Task<User> CreateUserAsync(Action<User>? customize = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Interlocked.Increment(ref _userSuffix);
            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = $"{SteamId}{suffix:D2}",
                SteamDisplayName = "Tester",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-200),
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
            db.Set<OutboxMessage>().RemoveRange(db.Set<OutboxMessage>());
            db.Set<Skinora.Transactions.Domain.Entities.Transaction>()
                .RemoveRange(db.Set<Skinora.Transactions.Domain.Entities.Transaction>());
            var seedIds = new[] { Skinora.Shared.Domain.Seed.SeedConstants.SystemUserId };
            db.Set<User>().RemoveRange(db.Set<User>().Where(u => !seedIds.Contains(u.Id)));
            db.Set<Skinora.Platform.Domain.Entities.SystemSetting>()
                .RemoveRange(db.Set<Skinora.Platform.Domain.Entities.SystemSetting>());
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

                // T45 — replace the Steam inventory stub with the in-test fake
                // so the seller's items are predictable from test code.
                services.RemoveAll<ISteamInventoryReader>();
                services.AddSingleton<ISteamInventoryReader>(_inventory);
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
