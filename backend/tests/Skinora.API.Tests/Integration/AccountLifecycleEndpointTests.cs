using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
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
using Skinora.Auth.Domain.Entities;
using Skinora.Notifications.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

public class AccountLifecycleEndpointTests
    : IClassFixture<AccountLifecycleEndpointTests.Factory>
{
    private const string TestSecret = "lifecycle-test-secret-key-minimum-32-chars!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string SteamId = "76561198222444000";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public AccountLifecycleEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ---------- /users/me/deactivate ----------

    [Fact]
    public async Task Deactivate_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/users/me/deactivate", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_NoActiveTransactions_Succeeds_SetsFlagRevokesTokensClearsCookie()
    {
        var user = await _factory.CreateUserAsync();
        var (_, tokenId) = await _factory.SeedRefreshTokenAsync(user.Id);

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PostAsync("/api/v1/users/me/deactivate", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.True(data.TryGetProperty("deactivatedAt", out _));
        Assert.Contains("deaktif", data.GetProperty("message").GetString()!,
            StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var reloaded = await db.Set<User>().AsNoTracking()
            .FirstAsync(u => u.Id == user.Id);
        Assert.True(reloaded.IsDeactivated);
        Assert.NotNull(reloaded.DeactivatedAt);
        Assert.False(reloaded.IsDeleted);

        var token = await db.Set<RefreshToken>().AsNoTracking()
            .FirstAsync(t => t.Id == tokenId);
        Assert.True(token.IsRevoked);
        Assert.NotNull(token.RevokedAt);
        // Deactivate keeps the row — user can come back and see history.
        Assert.False(token.IsDeleted);

        var cleared = response.Headers.GetValues("Set-Cookie")
            .Any(v => v.StartsWith("refreshToken=")
                && v.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.True(cleared);
    }

    [Fact]
    public async Task Deactivate_WithActiveSellerTransaction_Returns422HasActiveTransactions()
    {
        var user = await _factory.CreateUserAsync();
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.Status = TransactionStatus.ITEM_ESCROWED;
        });

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PostAsync("/api/v1/users/me/deactivate", content: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("HAS_ACTIVE_TRANSACTIONS",
            body.GetProperty("error").GetProperty("code").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.Set<User>().AsNoTracking()
            .FirstAsync(u => u.Id == user.Id);
        Assert.False(reloaded.IsDeactivated);
    }

    [Fact]
    public async Task Deactivate_WithActiveBuyerTransaction_Returns422HasActiveTransactions()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = seller.Id;
            t.BuyerId = buyer.Id;
            t.Status = TransactionStatus.PAYMENT_RECEIVED;
        });

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);

        var response = await client.PostAsync("/api/v1/users/me/deactivate", content: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_WithOnlyTerminalTransactions_Succeeds()
    {
        var user = await _factory.CreateUserAsync();
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.Status = TransactionStatus.COMPLETED;
        });
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.Status = TransactionStatus.CANCELLED_BUYER;
            t.CancelledBy = CancelledByType.BUYER;
            t.CancelReason = "buyer cancelled";
            t.CancelledAt = DateTime.UtcNow;
        });

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PostAsync("/api/v1/users/me/deactivate", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- DELETE /users/me ----------

    [Fact]
    public async Task Delete_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(
            BuildDeleteRequest(confirmation: "SİL"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_MissingBody_Returns400ValidationError()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("VALIDATION_ERROR",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("sil")]
    [InlineData("SIL")]
    [InlineData("DELETE")]
    [InlineData("")]
    public async Task Delete_InvalidConfirmation_Returns400ValidationError(string confirmation)
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.SendAsync(BuildDeleteRequest(confirmation));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("VALIDATION_ERROR",
            body.GetProperty("error").GetProperty("code").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.Set<User>().AsNoTracking()
            .FirstAsync(u => u.Id == user.Id);
        Assert.False(reloaded.IsDeleted);
    }

    [Fact]
    public async Task Delete_WithActiveTransaction_Returns422HasActiveTransactions()
    {
        var user = await _factory.CreateUserAsync();
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.Status = TransactionStatus.ACCEPTED;
        });

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.SendAsync(BuildDeleteRequest("SİL"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("HAS_ACTIVE_TRANSACTIONS",
            body.GetProperty("error").GetProperty("code").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.Set<User>().AsNoTracking()
            .FirstAsync(u => u.Id == user.Id);
        Assert.False(reloaded.IsDeleted);
        Assert.Equal(user.SteamId, reloaded.SteamId);
    }

    [Fact]
    public async Task Delete_HappyPath_AnonymizesUser_Preferences_Deliveries_Tokens()
    {
        var originalSteamId = $"{SteamId}99";
        var user = await _factory.CreateUserAsync(u =>
        {
            u.SteamId = originalSteamId;
            u.SteamDisplayName = "Original Player";
            u.SteamAvatarUrl = "https://steamcdn.test/avatar.jpg";
            u.Email = "player@example.com";
            u.EmailVerifiedAt = DateTime.UtcNow;
            u.DefaultPayoutAddress = "TDefau1t4567abcdefghijkmnopqrs1234";
            u.DefaultRefundAddress = "TDefau1t4567abcdefghijkmnopqrs5678";
            u.SteamTradeUrl = "https://steamcommunity.com/tradeoffer/new/?partner=1&token=abc";
            u.SteamTradePartner = "1";
            u.SteamTradeAccessToken = "abc";
        });
        var (_, tokenId) = await _factory.SeedRefreshTokenAsync(
            user.Id, deviceInfo: "Chrome/Windows", ipAddress: "203.0.113.1");

        var (notificationId, deliveryId) = await _factory.SeedNotificationWithDeliveryAsync(
            user.Id, NotificationChannel.TELEGRAM, "tg-user-12345678");
        var preferenceId = await _factory.SeedPreferenceAsync(
            user.Id, NotificationChannel.TELEGRAM, "tg-user-12345678", enabled: true);
        var emailPrefId = await _factory.SeedPreferenceAsync(
            user.Id, NotificationChannel.EMAIL, "player@example.com", enabled: true);

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.SendAsync(BuildDeleteRequest("SİL"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.True(data.TryGetProperty("deletedAt", out _));
        Assert.Contains("silindi", data.GetProperty("message").GetString()!,
            StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // User is soft-deleted + anonymized. IgnoreQueryFilters because the
        // global filter hides IsDeleted rows.
        var reloaded = await db.Set<User>().IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(u => u.Id == user.Id);
        Assert.True(reloaded.IsDeleted);
        Assert.NotNull(reloaded.DeletedAt);
        Assert.StartsWith("ANON_", reloaded.SteamId);
        Assert.NotEqual(originalSteamId, reloaded.SteamId);
        Assert.True(reloaded.SteamId.Length <= 20);
        Assert.Equal("Deleted User", reloaded.SteamDisplayName);
        Assert.Null(reloaded.SteamAvatarUrl);
        Assert.Null(reloaded.Email);
        Assert.Null(reloaded.EmailVerifiedAt);
        Assert.Null(reloaded.DefaultPayoutAddress);
        Assert.Null(reloaded.DefaultRefundAddress);
        Assert.Null(reloaded.SteamTradeUrl);
        Assert.Null(reloaded.SteamTradePartner);
        Assert.Null(reloaded.SteamTradeAccessToken);

        // Preferences soft-deleted + ExternalId cleared.
        var prefs = await db.Set<UserNotificationPreference>()
            .IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.UserId == user.Id)
            .ToListAsync();
        Assert.Equal(2, prefs.Count);
        Assert.All(prefs, p =>
        {
            Assert.True(p.IsDeleted);
            Assert.NotNull(p.DeletedAt);
            Assert.Null(p.ExternalId);
            Assert.False(p.IsEnabled);
        });

        // NotificationDelivery TargetExternalId masked per 06 §6.2.
        var delivery = await db.Set<NotificationDelivery>()
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(d => d.Id == deliveryId);
        Assert.StartsWith("tg:***", delivery.TargetExternalId);
        Assert.DoesNotContain("12345678", delivery.TargetExternalId);

        // Refresh tokens revoked + soft-deleted + device info cleared.
        var refreshToken = await db.Set<RefreshToken>()
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(t => t.Id == tokenId);
        Assert.True(refreshToken.IsRevoked);
        Assert.NotNull(refreshToken.RevokedAt);
        Assert.True(refreshToken.IsDeleted);
        Assert.NotNull(refreshToken.DeletedAt);
        Assert.Null(refreshToken.DeviceInfo);
        Assert.Null(refreshToken.IpAddress);

        // Audit trail (Notification row itself) is preserved — 06 §6.2.
        var notification = await db.Set<Notification>()
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(n => n.Id == notificationId);
        Assert.False(notification.IsDeleted);

        // Refresh cookie cleared.
        var cleared = response.Headers.GetValues("Set-Cookie")
            .Any(v => v.StartsWith("refreshToken=")
                && v.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.True(cleared);

        _ = preferenceId;
        _ = emailPrefId;
    }

    [Fact]
    public async Task Delete_DeliveryMasking_EmailChannel_UsesFixedLiteral()
    {
        var user = await _factory.CreateUserAsync(u => u.Email = "alice@example.com");
        var (_, deliveryId) = await _factory.SeedNotificationWithDeliveryAsync(
            user.Id, NotificationChannel.EMAIL, "alice@example.com");

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.SendAsync(BuildDeleteRequest("SİL"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Set<NotificationDelivery>()
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(d => d.Id == deliveryId);
        Assert.Equal("***@***.com", delivery.TargetExternalId);
    }

    [Fact]
    public async Task Delete_TransactionHistoryPreserved_AuditTrailIntact()
    {
        var user = await _factory.CreateUserAsync();
        var tx = await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.Status = TransactionStatus.COMPLETED;
        });

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.SendAsync(BuildDeleteRequest("SİL"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var txAfter = await db.Set<Transaction>()
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        // Transaction row is untouched — FK still points at the anonymized user.
        Assert.Equal(user.Id, txAfter.SellerId);
        Assert.False(txAfter.IsDeleted);
    }

    // ---------- helpers ----------

    private HttpRequestMessage BuildDeleteRequest(string? confirmation)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me")
        {
            Content = JsonContent.Create(new { confirmation }),
        };
        return request;
    }

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

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;
        private int _userSuffix;
        private int _transactionSuffix;

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
                SteamId = $"{SteamId}{suffix:D2}",
                SteamDisplayName = "Tester",
                PreferredLanguage = "en",
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
                SellerPayoutAddress = "TDefau1t4567abcdefghijkmnopqrs1234",
                PaymentTimeoutMinutes = 60,
                CreatedAt = DateTime.UtcNow,
            };
            customize?.Invoke(tx);

            db.Set<Transaction>().Add(tx);
            await db.SaveChangesAsync();
            return tx;
        }

        public async Task<(string plainText, Guid tokenId)> SeedRefreshTokenAsync(
            Guid userId, string? deviceInfo = null, string? ipAddress = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain)));
            var token = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = hash,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                IsRevoked = false,
                DeviceInfo = deviceInfo,
                IpAddress = ipAddress,
            };
            db.Set<RefreshToken>().Add(token);
            await db.SaveChangesAsync();
            return (plain, token.Id);
        }

        public async Task<Guid> SeedPreferenceAsync(
            Guid userId, NotificationChannel channel, string externalId, bool enabled)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pref = new UserNotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Channel = channel,
                IsEnabled = enabled,
                ExternalId = externalId,
                VerifiedAt = DateTime.UtcNow,
            };
            db.Set<UserNotificationPreference>().Add(pref);
            await db.SaveChangesAsync();
            return pref.Id;
        }

        public async Task<(Guid notificationId, Guid deliveryId)> SeedNotificationWithDeliveryAsync(
            Guid userId, NotificationChannel channel, string targetExternalId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = NotificationType.BUYER_ACCEPTED,
                Title = "Test",
                Body = "Test body",
            };
            var delivery = new NotificationDelivery
            {
                Id = Guid.NewGuid(),
                NotificationId = notification.Id,
                Channel = channel,
                TargetExternalId = targetExternalId,
                Status = DeliveryStatus.PENDING,
            };
            db.Set<Notification>().Add(notification);
            db.Set<NotificationDelivery>().Add(delivery);
            await db.SaveChangesAsync();
            return (notification.Id, delivery.Id);
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Set<NotificationDelivery>().RemoveRange(db.Set<NotificationDelivery>());
            db.Set<Notification>().RemoveRange(db.Set<Notification>().IgnoreQueryFilters());
            db.Set<UserNotificationPreference>().RemoveRange(
                db.Set<UserNotificationPreference>().IgnoreQueryFilters());
            db.Set<RefreshToken>().RemoveRange(
                db.Set<RefreshToken>().IgnoreQueryFilters());
            db.Set<Transaction>().RemoveRange(
                db.Set<Transaction>().IgnoreQueryFilters());
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

                services.RemoveAll<IRefreshTokenCache>();
                services.AddSingleton<IRefreshTokenCache, NullRefreshTokenCache>();
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
