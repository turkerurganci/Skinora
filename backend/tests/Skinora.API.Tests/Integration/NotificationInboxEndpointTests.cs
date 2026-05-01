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
using Skinora.Notifications.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

public class NotificationInboxEndpointTests : IClassFixture<NotificationInboxEndpointTests.Factory>
{
    private const string TestSecret = "notif-inbox-test-secret-key-minimum-32-chars!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string SteamIdPrefix = "76561198888777";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public NotificationInboxEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ---------- GET /notifications ----------

    [Fact]
    public async Task GetList_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/notifications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetList_Authenticated_ReturnsOnlyOwnNotificationsNewestFirst()
    {
        var owner = await _factory.CreateUserAsync();
        var stranger = await _factory.CreateUserAsync();

        var older = await _factory.CreateNotificationAsync(owner.Id, n =>
        {
            n.Type = NotificationType.BUYER_ACCEPTED;
            n.Title = "Alıcı işlemi kabul etti";
            n.CreatedAt = DateTime.UtcNow.AddHours(-2);
        });
        var newer = await _factory.CreateNotificationAsync(owner.Id, n =>
        {
            n.Type = NotificationType.PAYMENT_RECEIVED;
            n.Title = "Ödeme doğrulandı";
            n.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
        });
        await _factory.CreateNotificationAsync(stranger.Id, n =>
        {
            n.Type = NotificationType.TRANSACTION_INVITE;
            n.Title = "Yeni davet";
        });

        var client = BuildAuthenticatedClient(owner.Id, owner.SteamId);
        var response = await client.GetAsync("/api/v1/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(20, data.GetProperty("pageSize").GetInt32());

        var items = data.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());

        var first = items[0];
        Assert.Equal(newer.Id.ToString(), first.GetProperty("id").GetString());
        Assert.Equal("PAYMENT_RECEIVED", first.GetProperty("type").GetString());
        Assert.Equal("Ödeme doğrulandı", first.GetProperty("message").GetString());
        Assert.False(first.GetProperty("isRead").GetBoolean());

        // TransactionId is null in the seed (creating a valid Transaction has
        // many CHECK constraint dependencies — the mapping itself is covered
        // by NotificationTargetMapperTests, this assertion just confirms the
        // controller surfaces null/null when no transaction is attached).
        Assert.Equal(JsonValueKind.Null, first.GetProperty("targetType").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("targetId").ValueKind);

        var second = items[1];
        Assert.Equal(older.Id.ToString(), second.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetList_NullTransactionId_TargetTypeIsNull()
    {
        var owner = await _factory.CreateUserAsync();
        await _factory.CreateNotificationAsync(owner.Id, n =>
        {
            n.Type = NotificationType.ADMIN_STEAM_BOT_ISSUE;
            n.Title = "Steam bot offline";
            n.TransactionId = null;
        });

        var client = BuildAuthenticatedClient(owner.Id, owner.SteamId);
        var response = await client.GetAsync("/api/v1/notifications");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var item = body.GetProperty("data").GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, item.GetProperty("targetType").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("targetId").ValueKind);
    }

    [Fact]
    public async Task GetList_PaginatesCorrectly()
    {
        var owner = await _factory.CreateUserAsync();
        for (var i = 0; i < 5; i++)
        {
            await _factory.CreateNotificationAsync(owner.Id, n =>
            {
                n.Type = NotificationType.BUYER_ACCEPTED;
                n.Title = $"row-{i}";
                n.CreatedAt = DateTime.UtcNow.AddMinutes(-i);
            });
        }

        var client = BuildAuthenticatedClient(owner.Id, owner.SteamId);
        var response = await client.GetAsync("/api/v1/notifications?page=2&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(5, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, data.GetProperty("page").GetInt32());
        Assert.Equal(2, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task GetList_PageSizeOver100_ClampsTo100()
    {
        var owner = await _factory.CreateUserAsync();
        await _factory.CreateNotificationAsync(owner.Id);

        var client = BuildAuthenticatedClient(owner.Id, owner.SteamId);
        var response = await client.GetAsync("/api/v1/notifications?pageSize=500");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(100, body.GetProperty("data").GetProperty("pageSize").GetInt32());
    }

    // ---------- GET /notifications/unread-count ----------

    [Fact]
    public async Task UnreadCount_Authenticated_ReturnsOnlyOwnUnread()
    {
        var owner = await _factory.CreateUserAsync();
        var stranger = await _factory.CreateUserAsync();

        await _factory.CreateNotificationAsync(owner.Id, n => n.IsRead = false);
        await _factory.CreateNotificationAsync(owner.Id, n => n.IsRead = false);
        await _factory.CreateNotificationAsync(owner.Id, n =>
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow.AddMinutes(-1);
        });
        await _factory.CreateNotificationAsync(stranger.Id, n => n.IsRead = false);

        var client = BuildAuthenticatedClient(owner.Id, owner.SteamId);
        var response = await client.GetAsync("/api/v1/notifications/unread-count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(2, body.GetProperty("data").GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task UnreadCount_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/notifications/unread-count");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- POST /notifications/mark-all-read ----------

    [Fact]
    public async Task MarkAllRead_FlipsOnlyUnreadAndReturnsCount()
    {
        var owner = await _factory.CreateUserAsync();
        var stranger = await _factory.CreateUserAsync();

        await _factory.CreateNotificationAsync(owner.Id, n => n.IsRead = false);
        await _factory.CreateNotificationAsync(owner.Id, n => n.IsRead = false);
        await _factory.CreateNotificationAsync(owner.Id, n =>
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow.AddMinutes(-10);
        });
        var strangerNotif = await _factory.CreateNotificationAsync(
            stranger.Id, n => n.IsRead = false);

        var client = BuildAuthenticatedClient(owner.Id, owner.SteamId);
        var response = await client.PostAsync(
            "/api/v1/notifications/mark-all-read", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(2, body.GetProperty("data").GetProperty("markedCount").GetInt32());

        var ownerRows = await _factory.GetNotificationsAsync(owner.Id);
        Assert.All(ownerRows, n => Assert.True(n.IsRead));
        Assert.All(ownerRows, n => Assert.NotNull(n.ReadAt));

        var strangerRow = await _factory.GetNotificationAsync(strangerNotif.Id);
        Assert.False(strangerRow.IsRead);
    }

    [Fact]
    public async Task MarkAllRead_NoUnread_ReturnsZero()
    {
        var owner = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(owner.Id, owner.SteamId);

        var response = await client.PostAsync(
            "/api/v1/notifications/mark-all-read", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(0, body.GetProperty("data").GetProperty("markedCount").GetInt32());
    }

    // ---------- PUT /notifications/:id/read ----------

    [Fact]
    public async Task MarkRead_OwnUnreadNotification_FlipsToRead()
    {
        var owner = await _factory.CreateUserAsync();
        var notification = await _factory.CreateNotificationAsync(
            owner.Id, n => n.IsRead = false);

        var client = BuildAuthenticatedClient(owner.Id, owner.SteamId);
        var response = await client.PutAsync(
            $"/api/v1/notifications/{notification.Id}/read", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var persisted = await _factory.GetNotificationAsync(notification.Id);
        Assert.True(persisted.IsRead);
        Assert.NotNull(persisted.ReadAt);
    }

    [Fact]
    public async Task MarkRead_AlreadyRead_OkIdempotent()
    {
        var owner = await _factory.CreateUserAsync();
        var readAt = DateTime.UtcNow.AddMinutes(-30);
        var notification = await _factory.CreateNotificationAsync(owner.Id, n =>
        {
            n.IsRead = true;
            n.ReadAt = readAt;
        });

        var client = BuildAuthenticatedClient(owner.Id, owner.SteamId);
        var response = await client.PutAsync(
            $"/api/v1/notifications/{notification.Id}/read", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var persisted = await _factory.GetNotificationAsync(notification.Id);
        Assert.True(persisted.IsRead);
        // Original ReadAt preserved — second call must not bump the timestamp.
        Assert.Equal(readAt.ToUniversalTime(),
            persisted.ReadAt!.Value.ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task MarkRead_UnknownId_Returns404WithCode()
    {
        var owner = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(owner.Id, owner.SteamId);

        var response = await client.PutAsync(
            $"/api/v1/notifications/{Guid.NewGuid()}/read", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("NOTIFICATION_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MarkRead_OtherUsersNotification_Returns403()
    {
        var owner = await _factory.CreateUserAsync();
        var stranger = await _factory.CreateUserAsync();
        var strangerNotif = await _factory.CreateNotificationAsync(stranger.Id);

        var client = BuildAuthenticatedClient(owner.Id, owner.SteamId);
        var response = await client.PutAsync(
            $"/api/v1/notifications/{strangerNotif.Id}/read", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("FORBIDDEN", body.GetProperty("error").GetProperty("code").GetString());

        var persisted = await _factory.GetNotificationAsync(strangerNotif.Id);
        Assert.False(persisted.IsRead);
    }

    [Fact]
    public async Task MarkRead_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsync(
            $"/api/v1/notifications/{Guid.NewGuid()}/read", content: null);

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
                SteamDisplayName = "Tester",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
            };
            customize?.Invoke(user);

            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public async Task<Notification> CreateNotificationAsync(
            Guid userId, Action<Notification>? customize = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = NotificationType.BUYER_ACCEPTED,
                Title = "Alıcı işlemi kabul etti",
                Body = "Daha uzun açıklama metni.",
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
            };
            customize?.Invoke(notification);
            var desiredCreatedAt = notification.CreatedAt;

            db.Set<Notification>().Add(notification);
            await db.SaveChangesAsync();

            // AppDbContext.UpdateAuditFields overwrites CreatedAt to UtcNow on
            // the Added pipeline — replay with a second save so tests can
            // control CreatedAt for ordering assertions (mirrors the helper
            // in UserProfileEndpointTests).
            if (notification.CreatedAt != desiredCreatedAt)
            {
                notification.CreatedAt = desiredCreatedAt;
                await db.SaveChangesAsync();
            }
            return notification;
        }

        public async Task<Notification> GetNotificationAsync(Guid id)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<Notification>().AsNoTracking().FirstAsync(n => n.Id == id);
        }

        public async Task<List<Notification>> GetNotificationsAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<Notification>()
                .AsNoTracking()
                .Where(n => n.UserId == userId)
                .ToListAsync();
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<Notification>().RemoveRange(
                db.Set<Notification>().IgnoreQueryFilters().ToList());
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
