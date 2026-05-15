using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Startup;
using Skinora.API.Tests.Common;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Webhooks;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T68 — Integration coverage for the Steam sidecar webhook surface
/// (<c>POST /api/v1/webhooks/steam/{bot-events,trade-events}</c>). Exercises
/// the <c>WebhookSignatureMiddleware</c> across all four 401 paths (missing
/// headers, bad signature, stale timestamp, replay) plus a happy-path e2e flow
/// that drives the transaction state machine.
/// </summary>
public sealed class SteamWebhookEndpointTests : IClassFixture<SteamWebhookEndpointTests.Factory>
{
    private const string TestSecret = "skinora-test-webhook-shared-secret-32!!";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public SteamWebhookEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task TradeEvents_MissingHeaders_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/webhooks/steam/trade-events",
            MakeTradeEnvelope("trade_offer.sent", Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TradeEvents_InvalidSignature_Returns401()
    {
        var client = _factory.CreateClient();
        var payload = MakeTradeEnvelope("trade_offer.sent", Guid.NewGuid());
        var body = JsonSerializer.Serialize(payload, JsonOptions);

        using var request = BuildRequest("/api/v1/webhooks/steam/trade-events", body,
            timestamp: DateTime.UtcNow.ToString("O"),
            nonce: Guid.NewGuid().ToString("N"),
            signature: "deadbeef");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TradeEvents_StaleTimestamp_Returns401()
    {
        var client = _factory.CreateClient();
        var payload = MakeTradeEnvelope("trade_offer.sent", Guid.NewGuid());
        var body = JsonSerializer.Serialize(payload, JsonOptions);
        var staleTimestamp = DateTime.UtcNow.AddMinutes(-15).ToString("O");
        var nonce = Guid.NewGuid().ToString("N");

        using var request = BuildRequest("/api/v1/webhooks/steam/trade-events", body,
            timestamp: staleTimestamp,
            nonce: nonce,
            signature: Sign(staleTimestamp, nonce, body));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TradeEvents_NonceReplay_SecondRequestReturns401()
    {
        var client = _factory.CreateClient();
        var payload = MakeTradeEnvelope("trade_offer.sent", Guid.NewGuid());
        var body = JsonSerializer.Serialize(payload, JsonOptions);
        var timestamp = DateTime.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");
        var signature = Sign(timestamp, nonce, body);

        using (var first = BuildRequest("/api/v1/webhooks/steam/trade-events", body, timestamp, nonce, signature))
        {
            var firstResponse = await client.SendAsync(first);
            // First call: middleware accepts, controller swallows unknown txn.
            Assert.True(firstResponse.StatusCode is HttpStatusCode.OK,
                $"First call expected 200, got {firstResponse.StatusCode}.");
        }

        using var second = BuildRequest("/api/v1/webhooks/steam/trade-events", body, timestamp, nonce, signature);
        var secondResponse = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Unauthorized, secondResponse.StatusCode);
    }

    [Fact]
    public async Task TradeEvents_HappyPath_DrivesStateMachine()
    {
        var ids = await _factory.SeedAcceptedTransactionWithEscrowOfferAsync();
        var client = _factory.CreateClient();

        var envelope = new
        {
            @event = "trade_offer.accepted",
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new
            {
                direction = "escrow",
                offerId = ids.OfferId,
                botAccountName = ids.BotAccountName,
                newState = 3,
                oldState = 2,
            },
        };
        var body = JsonSerializer.Serialize(envelope, JsonOptions);
        var timestamp = DateTime.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");
        var signature = Sign(timestamp, nonce, body);

        using var request = BuildRequest("/api/v1/webhooks/steam/trade-events", body, timestamp, nonce, signature);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await _factory.AssertTransactionStatusAsync(ids.TransactionId, TransactionStatus.ITEM_ESCROWED);
        await _factory.AssertTradeOfferStatusAsync(ids.OfferId, TradeOfferStatus.ACCEPTED);
    }

    [Fact]
    public async Task BotEvents_HappyPath_Returns200()
    {
        var client = _factory.CreateClient();
        var envelope = new
        {
            @event = "bot.session_failed",
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new
            {
                accountName = "EscrowBot-99",
                reason = "InvalidPassword",
                status = "FAILED",
            },
        };
        var body = JsonSerializer.Serialize(envelope, JsonOptions);
        var timestamp = DateTime.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");

        using var request = BuildRequest("/api/v1/webhooks/steam/bot-events", body, timestamp, nonce,
            Sign(timestamp, nonce, body));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpRequestMessage BuildRequest(
        string path, string body, string timestamp, string nonce, string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Signature", signature);
        request.Headers.Add("X-Timestamp", timestamp);
        request.Headers.Add("X-Nonce", nonce);
        request.Headers.Add("X-Correlation-Id", "test-corr");
        return request;
    }

    private static string Sign(string timestamp, string nonce, string body)
    {
        var key = Encoding.UTF8.GetBytes(TestSecret);
        var payload = Encoding.UTF8.GetBytes($"{timestamp}{nonce}{body}");
        return Convert.ToHexString(HMACSHA256.HashData(key, payload)).ToLowerInvariant();
    }

    private static object MakeTradeEnvelope(string @event, Guid transactionId) => new
    {
        @event,
        timestamp = DateTime.UtcNow.ToString("O"),
        data = new
        {
            transactionId,
            direction = "escrow",
            botAccountName = "EscrowBot-99",
            offerId = "1234",
            status = "sent",
            attempts = 1,
        },
    };

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

        public async Task<(Guid TransactionId, string OfferId, string BotAccountName)> SeedAcceptedTransactionWithEscrowOfferAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000000201", SteamDisplayName = "Seller" };
            var buyer = new User { Id = Guid.NewGuid(), SteamId = "76561198000000202", SteamDisplayName = "Buyer" };
            var bot = new PlatformSteamBot
            {
                Id = Guid.NewGuid(),
                SteamId = "76561198099999201",
                DisplayName = "EscrowBot-99",
                Status = PlatformSteamBotStatus.ACTIVE,
            };

            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                Status = TransactionStatus.TRADE_OFFER_SENT_TO_SELLER,
                SellerId = seller.Id,
                BuyerId = buyer.Id,
                EscrowBotId = bot.Id,
                EscrowBotAssetId = "asset-on-bot",
                BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
                TargetBuyerSteamId = buyer.SteamId,
                BuyerRefundAddress = "TKnEzG4qX5n6ZRSeller7B9C2D3E4F5G6H7",
                ItemAssetId = "asset-1",
                ItemClassId = "cls",
                ItemInstanceId = "inst",
                ItemName = "AK-47",
                ItemIconUrl = "https://cdn.test/ak.png",
                StablecoinType = StablecoinType.USDT,
                Price = 100m,
                CommissionRate = 0.03m,
                CommissionAmount = 3m,
                TotalAmount = 103m,
                SellerPayoutAddress = "TKnEzG4qX5n6ZRBuyer7B9C2D3E4F5G6H7",
            };

            var offer = new TradeOffer
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                PlatformSteamBotId = bot.Id,
                Direction = TradeOfferDirection.TO_SELLER,
                SteamTradeOfferId = "8800",
                Status = TradeOfferStatus.SENT,
                SentAt = DateTime.UtcNow,
            };

            db.Set<User>().AddRange(seller, buyer);
            db.Set<PlatformSteamBot>().Add(bot);
            db.Set<Transaction>().Add(tx);
            db.Set<TradeOffer>().Add(offer);
            await db.SaveChangesAsync();

            return (tx.Id, offer.SteamTradeOfferId!, bot.DisplayName);
        }

        public async Task AssertTransactionStatusAsync(Guid id, TransactionStatus expected)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var status = await db.Set<Transaction>().Where(t => t.Id == id)
                .Select(t => t.Status).SingleAsync();
            Assert.Equal(expected, status);
        }

        public async Task AssertTradeOfferStatusAsync(string offerId, TradeOfferStatus expected)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var status = await db.Set<TradeOffer>().Where(t => t.SteamTradeOfferId == offerId)
                .Select(t => t.Status).SingleAsync();
            Assert.Equal(expected, status);
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // ProcessedNonce is IAppendOnly — ChangeTracker.RemoveRange would
            // hit EnforceAppendOnly. Raw ExecuteDelete bypasses the guard,
            // which is acceptable in test fixtures.
            db.Set<ProcessedNonce>().ExecuteDelete();
            db.Set<TradeOffer>().ExecuteDelete();
            db.Set<Transaction>().IgnoreQueryFilters().ExecuteDelete();
            db.Set<PlatformSteamBot>().IgnoreQueryFilters().ExecuteDelete();
            db.Set<User>().IgnoreQueryFilters().ExecuteDelete();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Server=(local);Database=SkinoraTest;Integrated Security=true;TrustServerCertificate=true");
            builder.UseSetting("Hangfire:DashboardEnabled", "false");

            builder.UseSetting("Jwt:Secret", "test-webhook-jwt-secret-32-chars-long!!");
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

            builder.UseSetting("SteamSidecar:BaseUrl", "http://localhost:65500");
            builder.UseSetting("SteamSidecar:InternalKey", "test-internal-key");

            // T68 — webhook secret used by the middleware.
            builder.UseSetting("Webhook:SteamSharedSecret", TestSecret);
            builder.UseSetting("Webhook:ReplayWindowSeconds", "300");
            builder.UseSetting("Webhook:NonceRetentionSeconds", "3600");

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
