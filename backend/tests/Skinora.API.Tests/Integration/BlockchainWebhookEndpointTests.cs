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
using Skinora.Shared.Persistence.Outbox;
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
    public async Task PaymentDetected_SameTxHashDifferentEventIndex_CreatesSeparateRows()
    {
        // WP10 (08 §3.4) — a transaction carrying two Transfer events to the
        // deposit address is recorded once per (TxHash, EventIndex). The former
        // TxHash-only dedup collapsed these to a single row, silently dropping
        // the second transfer.
        var ids = await _factory.SeedTransactionWithPaymentAddressAsync();
        var client = _factory.CreateClient();
        var txHash = "0xMulti" + Guid.NewGuid().ToString("N");

        var event0 = MakeDetectedEnvelope(ids.PaymentAddressId, ids.TransactionId,
            amount: "60.000000", eventIndex: 0, txHash: txHash);
        var event1 = MakeDetectedEnvelope(ids.PaymentAddressId, ids.TransactionId,
            amount: "40.000000", eventIndex: 1, txHash: txHash);

        var r0 = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected", event0);
        var r1 = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected", event1);
        Assert.Equal(HttpStatusCode.OK, r0.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        // Re-delivering event0 is still idempotent on its own (TxHash, EventIndex).
        var r0Again = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected", event0);
        var json = await r0Again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Idempotent", json.GetProperty("data").GetProperty("result").GetString());

        Assert.Equal(2, await _factory.CountBlockchainTransactionsAsync(txHash));
        var row0 = await _factory.GetBlockchainTransactionByEventAsync(txHash, 0);
        var row1 = await _factory.GetBlockchainTransactionByEventAsync(txHash, 1);
        Assert.Equal(60m, row0!.Amount);
        Assert.Equal(40m, row1!.Amount);
    }

    [Fact]
    public async Task PaymentConfirmed_MatchesTheRowForItsEventIndex()
    {
        // WP10 — confirmation must flip the row for the specific event index,
        // not the first row sharing the TxHash (which would mis-validate the
        // amount in a multi-transfer transaction).
        var ids = await _factory.SeedTransactionWithPaymentAddressAsync();
        var client = _factory.CreateClient();
        var txHash = "0xMulti" + Guid.NewGuid().ToString("N");

        await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected",
            MakeDetectedEnvelope(ids.PaymentAddressId, ids.TransactionId, amount: "100.000000", eventIndex: 0, txHash: txHash));
        await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected",
            MakeDetectedEnvelope(ids.PaymentAddressId, ids.TransactionId, amount: "100.000000", eventIndex: 1, txHash: txHash));

        // Confirm only event index 1.
        var response = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-confirmed",
            MakeConfirmedEnvelope(ids.PaymentAddressId, ids.TransactionId, txHash, eventIndex: 1));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row1 = await _factory.GetBlockchainTransactionByEventAsync(txHash, 1);
        var row0 = await _factory.GetBlockchainTransactionByEventAsync(txHash, 0);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, row1!.Status);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, row0!.Status); // untouched
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

    // ─── T72 — Amount validation end-to-end (02 §4.4, 08 §3.4) ─────────

    [Fact]
    public async Task PaymentConfirmed_ExactAmount_AdvancesStateAndPublishesPaymentReceivedEvent()
    {
        var ids = await _factory.SeedTransactionWithPaymentAddressAsync();
        var client = _factory.CreateClient();

        var detected = MakeDetectedEnvelope(ids.PaymentAddressId, ids.TransactionId);
        await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected", detected);

        var confirmed = MakeConfirmedEnvelope(ids.PaymentAddressId, ids.TransactionId, detected.data.txHash);
        var response = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-confirmed", confirmed);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tx = await _factory.GetTransactionAsync(ids.TransactionId);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, tx!.Status);
        Assert.NotNull(tx.PaymentReceivedAt);

        // PaymentReceivedEvent on outbox (T44 K2 wiring).
        var outbox = await _factory.GetOutboxEventTypesForTransactionAsync(ids.TransactionId);
        Assert.Contains("PaymentReceivedEvent", outbox);
        // No refund-intent rows.
        var refundCount = await _factory.CountRefundIntentsAsync(ids.TransactionId);
        Assert.Equal(0, refundCount);
    }

    [Fact]
    public async Task PaymentConfirmed_Underpayment_QueuesIncorrectAmountRefundAndBuyerEvent()
    {
        var ids = await _factory.SeedTransactionWithPaymentAddressAsync();
        var client = _factory.CreateClient();

        // Buyer sends 50 USDT when 100 was expected — under-payment branch.
        var detected = MakeDetectedEnvelope(ids.PaymentAddressId, ids.TransactionId, amount: "50.000000");
        await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected", detected);
        var confirmed = MakeConfirmedEnvelope(ids.PaymentAddressId, ids.TransactionId, detected.data.txHash);

        var response = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-confirmed", confirmed);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tx = await _factory.GetTransactionAsync(ids.TransactionId);
        // 02 §4.4 — state stays in ITEM_ESCROWED so the timeout countdown continues.
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, tx!.Status);

        var refund = await _factory.GetSingleRefundIntentAsync(ids.TransactionId, BlockchainTransactionType.INCORRECT_AMOUNT_REFUND);
        Assert.Equal(50m, refund.Amount);
        Assert.Equal(BlockchainTransactionStatus.PENDING, refund.Status);
        Assert.Equal(detected.data.fromAddress, refund.ToAddress);
        Assert.Null(refund.PaymentAddressId);

        var outbox = await _factory.GetOutboxEventTypesForTransactionAsync(ids.TransactionId);
        Assert.Contains("BuyerPaymentInsufficientEvent", outbox);
        Assert.DoesNotContain("PaymentReceivedEvent", outbox);
    }

    [Fact]
    public async Task PaymentConfirmed_UnderpaymentBelowThreshold_RaisesAdminAlertOnly()
    {
        var ids = await _factory.SeedTransactionWithPaymentAddressAsync();
        var client = _factory.CreateClient();

        // Below threshold: received 3 USDT, gas fee estimate 2, threshold = 2×2 = 4 ⇒ no refund.
        var detected = MakeDetectedEnvelope(ids.PaymentAddressId, ids.TransactionId, amount: "3.000000");
        await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected", detected);
        var confirmed = MakeConfirmedEnvelope(ids.PaymentAddressId, ids.TransactionId, detected.data.txHash);

        var response = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-confirmed", confirmed);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refundCount = await _factory.CountRefundIntentsAsync(ids.TransactionId);
        Assert.Equal(0, refundCount);
        var outbox = await _factory.GetOutboxEventTypesForTransactionAsync(ids.TransactionId);
        Assert.Contains("RefundBlockedAdminAlertEvent", outbox);
        Assert.DoesNotContain("BuyerPaymentInsufficientEvent", outbox);
    }

    [Fact]
    public async Task PaymentConfirmed_Overpayment_AdvancesStateAndQueuesExcessRefund()
    {
        var ids = await _factory.SeedTransactionWithPaymentAddressAsync();
        var client = _factory.CreateClient();

        // Buyer overpays by 10 USDT.
        var detected = MakeDetectedEnvelope(ids.PaymentAddressId, ids.TransactionId, amount: "110.000000");
        await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-detected", detected);
        var confirmed = MakeConfirmedEnvelope(ids.PaymentAddressId, ids.TransactionId, detected.data.txHash);

        var response = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/payment-confirmed", confirmed);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tx = await _factory.GetTransactionAsync(ids.TransactionId);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, tx!.Status);

        var refund = await _factory.GetSingleRefundIntentAsync(ids.TransactionId, BlockchainTransactionType.EXCESS_REFUND);
        Assert.Equal(10m, refund.Amount);
        Assert.Equal(BlockchainTransactionStatus.PENDING, refund.Status);

        var outbox = await _factory.GetOutboxEventTypesForTransactionAsync(ids.TransactionId);
        Assert.Contains("PaymentReceivedEvent", outbox);
        Assert.Contains("BuyerPaymentExcessRefundedEvent", outbox);
    }

    [Fact]
    public async Task WrongTokenIncoming_AboveThreshold_QueuesWrongTokenRefundAndBuyerEvent()
    {
        var ids = await _factory.SeedTransactionWithPaymentAddressAsync();
        var client = _factory.CreateClient();

        var envelope = MakeWrongTokenEnvelope(ids.PaymentAddressId, ids.TransactionId, ids.PaymentAddress, amount: "50.000000");
        var response = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/wrong-token", envelope);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refund = await _factory.GetSingleRefundIntentAsync(ids.TransactionId, BlockchainTransactionType.WRONG_TOKEN_REFUND);
        Assert.Equal(50m, refund.Amount);
        Assert.Equal(BlockchainTransactionStatus.PENDING, refund.Status);
        Assert.Equal(envelope.data.actualContractAddress, refund.ActualTokenAddress);
        // 06 §3.8 — refund row carries the *expected* stablecoin for the txn.
        Assert.Equal(StablecoinType.USDT, refund.Token);

        var outbox = await _factory.GetOutboxEventTypesForTransactionAsync(ids.TransactionId);
        Assert.Contains("WrongTokenRefundRequestedEvent", outbox);
    }

    [Fact]
    public async Task WrongTokenIncoming_BelowThreshold_RaisesAdminAlertOnly()
    {
        var ids = await _factory.SeedTransactionWithPaymentAddressAsync();
        var client = _factory.CreateClient();

        var envelope = MakeWrongTokenEnvelope(ids.PaymentAddressId, ids.TransactionId, ids.PaymentAddress, amount: "3.000000");
        var response = await SendSignedAsync(client, "/api/v1/webhooks/blockchain/wrong-token", envelope);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refundCount = await _factory.CountRefundIntentsAsync(ids.TransactionId);
        Assert.Equal(0, refundCount);
        var outbox = await _factory.GetOutboxEventTypesForTransactionAsync(ids.TransactionId);
        Assert.Contains("RefundBlockedAdminAlertEvent", outbox);
        Assert.DoesNotContain("WrongTokenRefundRequestedEvent", outbox);
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

    private static DetectedEnvelopeShape MakeDetectedEnvelope(
        Guid paymentAddressId,
        Guid transactionId,
        string amount = "100.000000",
        int eventIndex = 0,
        string? txHash = null) => new()
        {
            @event = "payment.detected",
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new DetectedDataShape
            {
                paymentAddressId = paymentAddressId,
                transactionId = transactionId,
                txHash = txHash ?? "0xDet" + Guid.NewGuid().ToString("N"),
                eventIndex = eventIndex,
                fromAddress = "TFromX1234567890123456789012345678",
                toAddress = "TToX1234567890123456789012345678901",
                contractAddress = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
                tokenSymbol = "USDT",
                amount = amount,
                blockTimestampMs = 1_778_000_000_000L,
                detectedAt = DateTime.UtcNow.ToString("O"),
            },
        };

    private static object MakeConfirmedEnvelope(
        Guid paymentAddressId, Guid transactionId, string txHash, int eventIndex = 0) => new
        {
            @event = "payment.confirmed",
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new
            {
                paymentAddressId,
                transactionId,
                txHash,
                eventIndex,
                blockNumber = 1_500_000L,
                confirmationCount = 20,
                confirmedAt = DateTime.UtcNow.ToString("O"),
            },
        };

    private static dynamic MakeWrongTokenEnvelope(
        Guid paymentAddressId,
        Guid transactionId,
        string paymentAddress,
        string amount) => new
        {
            @event = "payment.wrong_token",
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new
            {
                paymentAddressId,
                transactionId,
                txHash = "0xWrongToken" + Guid.NewGuid().ToString("N"),
                fromAddress = "TFromX1234567890123456789012345678",
                toAddress = paymentAddress,
                expectedContractAddress = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
                actualContractAddress = "TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8",
                actualTokenSymbol = "USDC",
                amount,
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
        public int eventIndex { get; set; }
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

        // WP10 — fetch the row for a specific (txHash, eventIndex) so per-event
        // dedup / confirmation-matching can be asserted independently.
        public async Task<BlockchainTransaction?> GetBlockchainTransactionByEventAsync(
            string txHash, int eventIndex)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<BlockchainTransaction>()
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.TxHash == txHash && b.EventIndex == eventIndex);
        }

        // ─── T72 amount validation helpers ──────────────────────────

        public async Task<Transaction?> GetTransactionAsync(Guid transactionId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<Transaction>().AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == transactionId);
        }

        public async Task<int> CountRefundIntentsAsync(Guid transactionId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<BlockchainTransaction>()
                .Where(b => b.TransactionId == transactionId
                    && (b.Type == BlockchainTransactionType.BUYER_REFUND
                     || b.Type == BlockchainTransactionType.EXCESS_REFUND
                     || b.Type == BlockchainTransactionType.WRONG_TOKEN_REFUND
                     || b.Type == BlockchainTransactionType.INCORRECT_AMOUNT_REFUND
                     || b.Type == BlockchainTransactionType.LATE_PAYMENT_REFUND))
                .CountAsync();
        }

        public async Task<BlockchainTransaction> GetSingleRefundIntentAsync(
            Guid transactionId, BlockchainTransactionType type)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<BlockchainTransaction>().AsNoTracking()
                .SingleAsync(b => b.TransactionId == transactionId && b.Type == type);
        }

        public async Task<List<string>> GetOutboxEventTypesForTransactionAsync(Guid transactionId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Outbox payload is JSON; cheap LIKE on the serialized TransactionId
            // is enough for an integration assertion (avoids deserialising every
            // event type discriminator).
            var marker = "\"TransactionId\":\"" + transactionId.ToString("D") + "\"";
            var rows = await db.Set<OutboxMessage>().AsNoTracking()
                .Where(m => m.Payload.Contains(marker))
                .Select(m => m.EventType)
                .ToListAsync();
            // EventType is "Namespace.Type, Assembly" — extract the
            // unqualified type name for assertion convenience.
            static string Simple(string fullyQualified)
            {
                // Strip ", Assembly" suffix first, then split on '.' for the
                // type name.
                var commaIdx = fullyQualified.IndexOf(',', StringComparison.Ordinal);
                var typeName = commaIdx > 0 ? fullyQualified[..commaIdx] : fullyQualified;
                var dotIdx = typeName.LastIndexOf('.');
                return dotIdx >= 0 ? typeName[(dotIdx + 1)..] : typeName;
            }
            return rows.Select(Simple).ToList();
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<ProcessedNonce>().ExecuteDelete();
            db.Set<OutboxMessage>().ExecuteDelete();
            // AuditLog rows (T72 below-threshold paths write them) reference
            // the SYSTEM user via ActorId — clear them before truncating users.
            db.Set<Skinora.Platform.Domain.Entities.AuditLog>().ExecuteDelete();
            db.Set<BlockchainTransaction>().ExecuteDelete();
            db.Set<PaymentAddress>().IgnoreQueryFilters().ExecuteDelete();
            db.Set<Transaction>().IgnoreQueryFilters().ExecuteDelete();
            // Preserve the EF-seeded SYSTEM user (06 §8.9, SeedConstants) —
            // RefundBlockedAlertService writes AuditLog with ActorId = SystemUserId
            // and AuditLog.ActorId is FK→User.
            db.Set<User>().IgnoreQueryFilters()
                .Where(u => u.Id != Skinora.Shared.Domain.Seed.SeedConstants.SystemUserId)
                .ExecuteDelete();
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
