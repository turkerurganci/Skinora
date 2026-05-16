using System.Linq.Expressions;
using System.Net;
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
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T71 — Integration coverage for the blockchain sidecar webhook surface
/// (<c>POST /api/v1/webhooks/blockchain/{payment-detected,payment-confirmed,wrong-token,spam-token}</c>).
/// Exercises the <c>WebhookSignatureMiddleware</c>'s blockchain branch
/// (path-scope expand from steam-only) plus end-to-end persistence into
/// <c>BlockchainTransactions</c>.
/// </summary>
public sealed class BlockchainWebhookEndpointTests : IClassFixture<BlockchainWebhookEndpointTests.Factory>
{
    private const string BlockchainSecret = "skinora-test-blockchain-webhook-32!!!";
    private const string SteamSecret = "skinora-test-steam-webhook-shared-32!";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public BlockchainWebhookEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task PaymentDetected_MissingHeaders_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/webhooks/blockchain/payment-detected",
            MakeDetectedEnvelope(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PaymentDetected_InvalidSignature_Returns401()
    {
        var client = _factory.CreateClient();
        var body = JsonSerializer.Serialize(MakeDetectedEnvelope(Guid.NewGuid(), Guid.NewGuid()), JsonOptions);

        using var request = BuildRequest("/api/v1/webhooks/blockchain/payment-detected", body,
            timestamp: DateTime.UtcNow.ToString("O"),
            nonce: Guid.NewGuid().ToString("N"),
            signature: "deadbeef");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PaymentDetected_SteamSecret_DoesNotAuthenticateBlockchain()
    {
        // Each sidecar has its own secret — a Steam-signed payload must not
        // be accepted on the blockchain path.
        var client = _factory.CreateClient();
        var body = JsonSerializer.Serialize(MakeDetectedEnvelope(Guid.NewGuid(), Guid.NewGuid()), JsonOptions);
        var timestamp = DateTime.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");
        var steamSignature = Sign(SteamSecret, timestamp, nonce, body);

        using var request = BuildRequest("/api/v1/webhooks/blockchain/payment-detected", body, timestamp, nonce, steamSignature);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PaymentDetected_HappyPath_PersistsBlockchainTransactionRow()
    {
        var ids = await _factory.SeedTransactionWithPaymentAddressAsync();
        var client = _factory.CreateClient();

        var envelope = MakeDetectedEnvelope(ids.PaymentAddressId, ids.TransactionId);
        var response = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected", envelope);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await _factory.GetBlockchainTransactionAsync(envelope.data.txHash);
        Assert.NotNull(row);
        Assert.Equal(BlockchainTransactionType.BUYER_PAYMENT, row!.Type);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, row.Status);
        Assert.Equal(100m, row.Amount);
        Assert.Equal(0, row.ConfirmationCount);
        Assert.Equal(ids.TransactionId, row.TransactionId);
        Assert.Equal(ids.PaymentAddressId, row.PaymentAddressId);
    }

    [Fact]
    public async Task PaymentDetected_DuplicateTxHash_ReturnsIdempotent()
    {
        var ids = await _factory.SeedTransactionWithPaymentAddressAsync();
        var client = _factory.CreateClient();
        var envelope = MakeDetectedEnvelope(ids.PaymentAddressId, ids.TransactionId);

        var first = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected", envelope);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected", envelope);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var json = await second.Content.ReadFromJsonAsync<JsonElement>();
        var dataElement = json.GetProperty("data");
        Assert.Equal("Idempotent", dataElement.GetProperty("result").GetString());

        var count = await _factory.CountBlockchainTransactionsAsync(envelope.data.txHash);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PaymentDetected_UnknownPaymentAddress_ReturnsUnknown()
    {
        var client = _factory.CreateClient();
        var envelope = MakeDetectedEnvelope(Guid.NewGuid(), Guid.NewGuid());

        var response = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected", envelope);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Unknown", json.GetProperty("data").GetProperty("result").GetString());
    }

    [Fact]
    public async Task PaymentConfirmed_FlipsExistingRowToConfirmed()
    {
        var ids = await _factory.SeedTransactionWithPaymentAddressAsync();
        var client = _factory.CreateClient();

        // Phase 1: detect.
        var detected = MakeDetectedEnvelope(ids.PaymentAddressId, ids.TransactionId);
        await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected", detected);

        // Phase 2: confirm.
        var confirmed = new
        {
            @event = "payment.confirmed",
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new
            {
                paymentAddressId = ids.PaymentAddressId,
                transactionId = ids.TransactionId,
                txHash = detected.data.txHash,
                blockNumber = 1_500_000L,
                confirmationCount = 20,
                confirmedAt = DateTime.UtcNow.ToString("O"),
            },
        };
        var response = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-confirmed", confirmed);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await _factory.GetBlockchainTransactionAsync(detected.data.txHash);
        Assert.NotNull(row);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, row!.Status);
        Assert.Equal(1_500_000L, row.BlockNumber);
        Assert.Equal(20, row.ConfirmationCount);
        Assert.NotNull(row.ConfirmedAt);
    }

    [Fact]
    public async Task WrongTokenIncoming_PersistsRowWithActualTokenAddress()
    {
        var ids = await _factory.SeedTransactionWithPaymentAddressAsync();
        var client = _factory.CreateClient();

        var envelope = new
        {
            @event = "payment.wrong_token",
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new
            {
                paymentAddressId = ids.PaymentAddressId,
                transactionId = ids.TransactionId,
                txHash = "0xWrongToken" + Guid.NewGuid().ToString("N"),
                fromAddress = "TFromX1234567890123456789012345678",
                toAddress = ids.PaymentAddress,
                expectedContractAddress = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
                actualContractAddress = "TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8",
                actualTokenSymbol = "USDC",
                amount = "50.000000",
                blockTimestampMs = 1_778_000_000_000L,
                detectedAt = DateTime.UtcNow.ToString("O"),
            },
        };
        var response = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/wrong-token", envelope);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await _factory.GetBlockchainTransactionAsync(envelope.data.txHash);
        Assert.NotNull(row);
        Assert.Equal(BlockchainTransactionType.WRONG_TOKEN_INCOMING, row!.Type);
        Assert.Equal("TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8", row.ActualTokenAddress);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, row.Status);
    }

    [Fact]
    public async Task SpamTokenIncoming_PersistsRowAtTerminalConfirmed()
    {
        var ids = await _factory.SeedTransactionWithPaymentAddressAsync();
        var client = _factory.CreateClient();

        var envelope = new
        {
            @event = "payment.spam_token",
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new
            {
                paymentAddressId = ids.PaymentAddressId,
                transactionId = ids.TransactionId,
                txHash = "0xSpam" + Guid.NewGuid().ToString("N"),
                fromAddress = "TFromX1234567890123456789012345678",
                toAddress = ids.PaymentAddress,
                expectedContractAddress = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
                actualContractAddress = "TSpamSpamSpamSpamSpamSpamSpamSpamSp",
                amount = "1000.000000",
                blockTimestampMs = 1_778_000_000_000L,
                detectedAt = DateTime.UtcNow.ToString("O"),
            },
        };
        var response = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/spam-token", envelope);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await _factory.GetBlockchainTransactionAsync(envelope.data.txHash);
        Assert.NotNull(row);
        Assert.Equal(BlockchainTransactionType.SPAM_TOKEN_INCOMING, row!.Type);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, row.Status);
        Assert.Equal(20, row.ConfirmationCount);
        Assert.NotNull(row.ConfirmedAt);
    }

    private static async Task<HttpResponseMessage> SendSignedAsync<T>(
        HttpClient client, string path, T payload)
    {
        var body = JsonSerializer.Serialize(payload, JsonOptions);
        var timestamp = DateTime.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");
        var signature = Sign(BlockchainSecret, timestamp, nonce, body);
        using var request = BuildRequest(path, body, timestamp, nonce, signature);
        return await client.SendAsync(request);
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

    private static string Sign(string secret, string timestamp, string nonce, string body)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var payload = Encoding.UTF8.GetBytes($"{timestamp}{nonce}{body}");
        return Convert.ToHexString(HMACSHA256.HashData(key, payload)).ToLowerInvariant();
    }

    private static DetectedEnvelopeShape MakeDetectedEnvelope(Guid paymentAddressId, Guid transactionId) => new()
    {
        @event = "payment.detected",
        timestamp = DateTime.UtcNow.ToString("O"),
        data = new DetectedDataShape
        {
            paymentAddressId = paymentAddressId,
            transactionId = transactionId,
            txHash = "0xDet" + Guid.NewGuid().ToString("N"),
            fromAddress = "TFromX1234567890123456789012345678",
            toAddress = "TToX1234567890123456789012345678901",
            contractAddress = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
            tokenSymbol = "USDT",
            amount = "100.000000",
            blockTimestampMs = 1_778_000_000_000L,
            detectedAt = DateTime.UtcNow.ToString("O"),
        },
    };

#pragma warning disable IDE1006 // Naming style — JSON wire shape mirrors camelCase
    private sealed class DetectedEnvelopeShape
    {
        public string @event { get; set; } = string.Empty;
        public string timestamp { get; set; } = string.Empty;
        public DetectedDataShape data { get; set; } = new();
    }

    private sealed class DetectedDataShape
    {
        public Guid paymentAddressId { get; set; }
        public Guid transactionId { get; set; }
        public string txHash { get; set; } = string.Empty;
        public string fromAddress { get; set; } = string.Empty;
        public string toAddress { get; set; } = string.Empty;
        public string contractAddress { get; set; } = string.Empty;
        public string tokenSymbol { get; set; } = string.Empty;
        public string amount { get; set; } = string.Empty;
        public long blockTimestampMs { get; set; }
        public string detectedAt { get; set; } = string.Empty;
    }
#pragma warning restore IDE1006

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

        public async Task<(Guid TransactionId, Guid PaymentAddressId, string PaymentAddress)> SeedTransactionWithPaymentAddressAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000000301", SteamDisplayName = "Seller" };
            var buyer = new User { Id = Guid.NewGuid(), SteamId = "76561198000000302", SteamDisplayName = "Buyer" };

            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                // ITEM_ESCROWED is when the bot has received the item and the
                // buyer is expected to pay — matches the PaymentAddress
                // monitoring window for the blockchain sidecar.
                Status = TransactionStatus.ITEM_ESCROWED,
                SellerId = seller.Id,
                BuyerId = buyer.Id,
                BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
                TargetBuyerSteamId = buyer.SteamId,
                BuyerRefundAddress = "TKnEzG4qX5n6ZRBuyer8B9C2D3E4F5G6H7Z",
                ItemAssetId = "asset-77",
                ItemClassId = "cls",
                ItemInstanceId = "inst",
                ItemName = "M4A1-S",
                ItemIconUrl = "https://cdn.test/m4.png",
                StablecoinType = StablecoinType.USDT,
                Price = 97m,
                CommissionRate = 0.03m,
                CommissionAmount = 3m,
                TotalAmount = 100m,
                SellerPayoutAddress = "TKnEzG4qX5n6ZRSeller8B9C2D3E4F5G6H8",
            };

            var paymentAddress = new PaymentAddress
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                Address = "TDeposit" + Guid.NewGuid().ToString("N").Substring(0, 26),
                HdWalletIndex = 42,
                ExpectedAmount = 100m,
                ExpectedToken = StablecoinType.USDT,
                MonitoringStatus = MonitoringStatus.ACTIVE,
                CreatedAt = DateTime.UtcNow,
            };

            db.Set<User>().AddRange(seller, buyer);
            db.Set<Transaction>().Add(tx);
            db.Set<PaymentAddress>().Add(paymentAddress);
            await db.SaveChangesAsync();

            return (tx.Id, paymentAddress.Id, paymentAddress.Address);
        }

        public async Task<BlockchainTransaction?> GetBlockchainTransactionAsync(string txHash)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<BlockchainTransaction>()
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.TxHash == txHash);
        }

        public async Task<int> CountBlockchainTransactionsAsync(string txHash)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<BlockchainTransaction>().CountAsync(b => b.TxHash == txHash);
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<ProcessedNonce>().ExecuteDelete();
            db.Set<BlockchainTransaction>().ExecuteDelete();
            db.Set<PaymentAddress>().IgnoreQueryFilters().ExecuteDelete();
            db.Set<Transaction>().IgnoreQueryFilters().ExecuteDelete();
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

            builder.UseSetting("BlockchainSidecar:BaseUrl", "http://localhost:65501");
            builder.UseSetting("BlockchainSidecar:InternalKey", "test-internal-key");

            // T71 — both sidecar webhook secrets registered so the path-scope
            // expand is exercised end-to-end.
            builder.UseSetting("Webhook:SteamSharedSecret", SteamSecret);
            builder.UseSetting("Webhook:BlockchainSharedSecret", BlockchainSecret);
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
