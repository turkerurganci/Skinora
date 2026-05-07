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
using Microsoft.AspNetCore.SignalR;
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
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// Integration coverage for <see cref="Skinora.Realtime.Hubs.TransactionsHub"/>
/// (T61 — 07 §11.1 RT1). Exercises the JWT query-param auth bridge, group
/// membership enforcement on <c>JoinTransaction</c>, and the round-trip from
/// the in-process publisher to a connected client.
/// </summary>
public class TransactionsHubEndpointTests : IClassFixture<TransactionsHubEndpointTests.Factory>
{
    private const string TestSecret = "hubs-test-secret-key-minimum-32-chars!!!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";

    private readonly Factory _factory;

    public TransactionsHubEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task Connect_Without_Token_Returns401()
    {
        // No bearer/query token at all → JWT auth pipeline should reject
        // before SignalR negotiation completes.
        var url = new Uri(_factory.Server.BaseAddress, "hubs/transactions");
        var connection = new HubConnectionBuilder()
            .WithUrl(url, options => ConfigureTransport(options, _factory))
            .Build();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => connection.StartAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task JoinTransaction_AsParticipant_Succeeds()
    {
        var (seller, buyer, tx) = await _factory.SeedTransactionAsync();

        await using var connection = await ConnectAsync(seller.Id, seller.SteamId);

        await connection.InvokeAsync("JoinTransaction", tx.Id);
        // No exception ⇒ join succeeded; group membership is server-internal.
    }

    [Fact]
    public async Task JoinTransaction_AsNonParticipant_ThrowsForbidden()
    {
        var (_, _, tx) = await _factory.SeedTransactionAsync();
        var outsider = await _factory.CreateUserAsync("76561198000099999");

        await using var connection = await ConnectAsync(outsider.Id, outsider.SteamId);

        var ex = await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync("JoinTransaction", tx.Id));
        Assert.Contains("TRANSACTION_FORBIDDEN", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JoinTransaction_UnknownTransaction_ThrowsNotFound()
    {
        var user = await _factory.CreateUserAsync("76561198000088888");

        await using var connection = await ConnectAsync(user.Id, user.SteamId);

        var ex = await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync("JoinTransaction", Guid.NewGuid()));
        Assert.Contains("TRANSACTION_NOT_FOUND", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publisher_Push_Reaches_Joined_Member()
    {
        var (seller, _, tx) = await _factory.SeedTransactionAsync();
        await using var connection = await ConnectAsync(seller.Id, seller.SteamId);

        var received = new TaskCompletionSource<TransactionStatusChangedDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<TransactionStatusChangedDto>("TransactionStatusChanged", payload =>
        {
            received.TrySetResult(payload);
        });

        await connection.InvokeAsync("JoinTransaction", tx.Id);

        // Drive the publisher manually rather than through the outbox — the
        // assertion is "publisher → hub group → client", not the dispatcher.
        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider
            .GetRequiredService<Skinora.Realtime.Application.ITransactionRealtimePublisher>();
        await publisher.PublishStatusChangedAsync(
            new Skinora.Realtime.Application.Contracts
                .TransactionRealtimePayloads.TransactionStatusChanged(
                    TransactionId: tx.Id,
                    FromStatus: TransactionStatus.CREATED,
                    ToStatus: TransactionStatus.ACCEPTED,
                    Timestamp: new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(tx.Id, payload.TransactionId);
        Assert.Equal("CREATED", payload.FromStatus);
        Assert.Equal("ACCEPTED", payload.ToStatus);
    }

    // ---------- helpers ----------

    private sealed record TransactionStatusChangedDto(
        Guid TransactionId,
        string FromStatus,
        string ToStatus,
        DateTime Timestamp);

    private async Task<HubConnection> ConnectAsync(Guid userId, string steamId)
    {
        var url = new Uri(_factory.Server.BaseAddress, "hubs/transactions");
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
        // Route the SignalR transport through TestServer's in-memory handler so
        // the WebApplicationFactory's pipeline (auth + hub) is exercised.
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

        public async Task<(User Seller, User Buyer, Transaction Transaction)> SeedTransactionAsync()
        {
            var seller = await CreateUserAsync($"76561198000{Random.Shared.Next(100000, 999999)}");
            var buyer = await CreateUserAsync($"76561198000{Random.Shared.Next(100000, 999999)}");

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                Status = TransactionStatus.ACCEPTED,
                SellerId = seller.Id,
                BuyerId = buyer.Id,
                BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
                TargetBuyerSteamId = buyer.SteamId,
                ItemAssetId = "ASSET",
                ItemClassId = "CLS",
                ItemName = "AK-47",
                StablecoinType = StablecoinType.USDT,
                Price = 10,
                CommissionRate = 0,
                CommissionAmount = 0,
                TotalAmount = 10,
                SellerPayoutAddress = "TRC20XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX",
                PaymentTimeoutMinutes = 60,
                AcceptedAt = DateTime.UtcNow,
            };
            db.Set<Transaction>().Add(tx);
            await db.SaveChangesAsync();
            return (seller, buyer, tx);
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<Transaction>().RemoveRange(db.Set<Transaction>());
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

            // T61 — disable the periodic broadcaster sweep; tests drive the
            // publisher path manually and the 30s loop would race the assertion.
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
