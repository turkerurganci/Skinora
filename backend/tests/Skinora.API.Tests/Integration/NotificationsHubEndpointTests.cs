using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Net;
using System.Security.Claims;
using System.Text;
using Medallion.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
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
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// Integration coverage for <see cref="Skinora.Realtime.Hubs.NotificationsHub"/>
/// (T62 — 07 §11.2 RT2). Exercises the JWT query-param auth bridge, automatic
/// per-user group join on connect, and the round-trip from the in-process
/// publisher to a connected client.
/// </summary>
public class NotificationsHubEndpointTests : IClassFixture<NotificationsHubEndpointTests.Factory>
{
    private const string TestSecret = "notif-hubs-test-secret-key-minimum-32-chars!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";

    private readonly Factory _factory;

    public NotificationsHubEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task Connect_Without_Token_Returns401()
    {
        var url = new Uri(_factory.Server.BaseAddress, "hubs/notifications");
        var connection = new HubConnectionBuilder()
            .WithUrl(url, options => ConfigureTransport(options, _factory))
            .Build();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => connection.StartAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Publisher_NewNotification_Reaches_Owner()
    {
        var owner = await _factory.CreateUserAsync("76561198000077001");

        await using var connection = await ConnectAsync(owner.Id, owner.SteamId);

        var received = new TaskCompletionSource<NewNotificationDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<NewNotificationDto>("NewNotification", payload =>
        {
            received.TrySetResult(payload);
        });

        // Give the connection a moment to complete OnConnectedAsync's group
        // join — the hub adds it on the way in but SignalR's Send is fire-and-
        // forget, so the connection completing does not strictly mean the
        // group membership is committed. A short wait makes the test robust on
        // slow CI runners.
        await Task.Delay(100);

        var notificationId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider
            .GetRequiredService<Skinora.Realtime.Application.INotificationRealtimePublisher>();
        await publisher.PublishNewNotificationAsync(
            owner.Id,
            new Skinora.Realtime.Application.Contracts.NotificationRealtimePayloads.NewNotification(
                Id: notificationId,
                Type: "PAYMENT_RECEIVED",
                Message: "Ödeme alındı",
                TargetType: "transaction",
                TargetId: Guid.NewGuid(),
                CreatedAt: new DateTime(2026, 5, 7, 12, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(notificationId, payload.Id);
        Assert.Equal("PAYMENT_RECEIVED", payload.Type);
        Assert.Equal("Ödeme alındı", payload.Message);
        Assert.Equal("transaction", payload.TargetType);
    }

    [Fact]
    public async Task Publisher_NewNotification_DoesNotReach_OtherUser()
    {
        var owner = await _factory.CreateUserAsync("76561198000077101");
        var stranger = await _factory.CreateUserAsync("76561198000077102");

        await using var ownerConnection = await ConnectAsync(owner.Id, owner.SteamId);
        await using var strangerConnection = await ConnectAsync(stranger.Id, stranger.SteamId);

        var ownerReceived = new TaskCompletionSource<NewNotificationDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var strangerReceived = new TaskCompletionSource<NewNotificationDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ownerConnection.On<NewNotificationDto>("NewNotification", p => ownerReceived.TrySetResult(p));
        strangerConnection.On<NewNotificationDto>("NewNotification", p => strangerReceived.TrySetResult(p));

        await Task.Delay(100);

        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider
            .GetRequiredService<Skinora.Realtime.Application.INotificationRealtimePublisher>();
        await publisher.PublishNewNotificationAsync(
            owner.Id,
            new Skinora.Realtime.Application.Contracts.NotificationRealtimePayloads.NewNotification(
                Id: Guid.NewGuid(),
                Type: "PAYMENT_RECEIVED",
                Message: "owner-only",
                TargetType: null,
                TargetId: null,
                CreatedAt: DateTime.UtcNow),
            CancellationToken.None);

        await ownerReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var hitStranger = await Task.WhenAny(strangerReceived.Task, Task.Delay(500));
        Assert.NotEqual(strangerReceived.Task, hitStranger);
    }

    [Fact]
    public async Task Publisher_UnreadCountChanged_Reaches_Owner()
    {
        var owner = await _factory.CreateUserAsync("76561198000077201");

        await using var connection = await ConnectAsync(owner.Id, owner.SteamId);

        var received = new TaskCompletionSource<UnreadCountDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<UnreadCountDto>("UnreadCountChanged", p => received.TrySetResult(p));

        await Task.Delay(100);

        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider
            .GetRequiredService<Skinora.Realtime.Application.INotificationRealtimePublisher>();
        await publisher.PublishUnreadCountChangedAsync(
            owner.Id,
            new Skinora.Realtime.Application.Contracts.NotificationRealtimePayloads.UnreadCountChanged(7),
            CancellationToken.None);

        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(7, payload.UnreadCount);
    }

    [Fact]
    public async Task Publisher_MaintenanceStatusChanged_Reaches_AllConnections()
    {
        var u1 = await _factory.CreateUserAsync("76561198000077301");
        var u2 = await _factory.CreateUserAsync("76561198000077302");

        await using var c1 = await ConnectAsync(u1.Id, u1.SteamId);
        await using var c2 = await ConnectAsync(u2.Id, u2.SteamId);

        var t1 = new TaskCompletionSource<MaintenanceDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t2 = new TaskCompletionSource<MaintenanceDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        c1.On<MaintenanceDto>("MaintenanceStatusChanged", p => t1.TrySetResult(p));
        c2.On<MaintenanceDto>("MaintenanceStatusChanged", p => t2.TrySetResult(p));

        await Task.Delay(100);

        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider
            .GetRequiredService<Skinora.Realtime.Application.INotificationRealtimePublisher>();
        await publisher.PublishMaintenanceStatusChangedAsync(
            new Skinora.Realtime.Application.Contracts
                .NotificationRealtimePayloads.MaintenanceStatusChanged(
                    Active: true,
                    Type: "PLATFORM_MAINTENANCE",
                    Message: "Bakım aktif",
                    PlannedEnd: new DateTime(2026, 5, 7, 18, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        var payload1 = await t1.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var payload2 = await t2.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(payload1.Active);
        Assert.True(payload2.Active);
        Assert.Equal("PLATFORM_MAINTENANCE", payload1.Type);
        Assert.Equal("PLATFORM_MAINTENANCE", payload2.Type);
    }

    // ---------- helpers ----------

    private sealed record NewNotificationDto(
        Guid Id,
        string Type,
        string Message,
        string? TargetType,
        Guid? TargetId,
        DateTime CreatedAt);

    private sealed record UnreadCountDto(int UnreadCount);

    private sealed record MaintenanceDto(
        bool Active,
        string? Type,
        string? Message,
        DateTime? PlannedEnd);

    private async Task<HubConnection> ConnectAsync(Guid userId, string steamId)
    {
        var url = new Uri(_factory.Server.BaseAddress, "hubs/notifications");
        var connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                ConfigureTransport(options, _factory);
                options.AccessTokenProvider = () => Task.FromResult<string?>(
                    IssueAccessToken(userId, steamId));
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    private static void ConfigureTransport(HttpConnectionOptions options, Factory factory)
    {
        options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
        options.Transports = HttpTransportType.LongPolling;
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
            Subject = new ClaimsIdentity(
            [
                new Claim(AuthClaimTypes.UserId, userId.ToString()),
                new Claim(AuthClaimTypes.SteamId, steamId),
                new Claim(AuthClaimTypes.Role, AuthRoles.User),
            ]),
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

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public async Task<User> CreateUserAsync(string steamId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = steamId,
                SteamDisplayName = $"User-{steamId[^4..]}",
            };
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<User>().RemoveRange(db.Set<User>());
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

            builder.UseSetting("Realtime:CountdownSync:Enabled", "false");

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
