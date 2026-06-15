using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.Transactions.Tests.Unit.Transfers;

/// <summary>
/// Unit coverage for <see cref="HttpBlockchainTransferClient"/> (T73, WP3).
/// Drives every routing branch (payout → /payout, refund family → /refund,
/// SWEEP → /sweep) and the response surface via a stub
/// <see cref="HttpMessageHandler"/>.
/// </summary>
public class HttpBlockchainTransferClientTests
{
    private const string SidecarBaseUrl = "http://blockchain-sidecar.test/";

    [Fact]
    public async Task Payout_Routes_To_PayoutEndpoint_AndReturnsSuccessTxHash()
    {
        HttpRequestMessage? observed = null;
        var handler = new RecordingHandler(async (req, _) =>
        {
            observed = req;
            await Task.CompletedTask;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { txHash = "tx-payout-1" }),
            };
        });
        var sut = BuildClient(handler);

        var request = new TransferBroadcastRequest(
            BlockchainTransactionId: Guid.NewGuid(),
            Type: BlockchainTransactionType.SELLER_PAYOUT,
            Token: StablecoinType.USDT,
            Amount: 100.5m,
            ToAddress: "TSeller000000000000000000000000000000",
            DepositIndex: null,
            DepositAddress: null);

        var result = await sut.BroadcastAsync(request, CancellationToken.None);

        Assert.Equal(TransferBroadcastStatus.Success, result.Status);
        Assert.Equal("tx-payout-1", result.TxHash);
        Assert.NotNull(observed);
        Assert.EndsWith("/api/transfer/payout", observed!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Refund_Routes_To_RefundEndpoint_WithDepositPayload()
    {
        string? capturedBody = null;
        var handler = new RecordingHandler(async (req, _) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { txHash = "tx-refund-1" }),
            };
        });
        var sut = BuildClient(handler);

        var request = new TransferBroadcastRequest(
            BlockchainTransactionId: Guid.NewGuid(),
            Type: BlockchainTransactionType.BUYER_REFUND,
            Token: StablecoinType.USDC,
            Amount: 50m,
            ToAddress: "TBuyerSource000000000000000000000000",
            DepositIndex: 7,
            DepositAddress: "TDeposit000000000000000000000000000000");

        var result = await sut.BroadcastAsync(request, CancellationToken.None);

        Assert.Equal(TransferBroadcastStatus.Success, result.Status);
        Assert.Equal("tx-refund-1", result.TxHash);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"depositIndex\":7", capturedBody);
        Assert.Contains("TDeposit000000000000000000000000000000", capturedBody);
        Assert.Contains("\"token\":\"USDC\"", capturedBody);
    }

    [Fact]
    public async Task Sweep_Routes_To_SweepEndpoint_WithDepositSourceAndHotDestination()
    {
        // WP3 — SWEEP must hit the dedicated /api/transfer/sweep endpoint with a
        // SweepBody (toHotWalletAddress + deposit index/address), NOT the refund
        // path. The row's ToAddress carries the hot-wallet destination.
        HttpRequestMessage? observed = null;
        string? capturedBody = null;
        var handler = new RecordingHandler(async (req, _) =>
        {
            observed = req;
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { txHash = "tx-sweep-1" }),
            };
        });
        var sut = BuildClient(handler);

        var request = new TransferBroadcastRequest(
            BlockchainTransactionId: Guid.NewGuid(),
            Type: BlockchainTransactionType.SWEEP,
            Token: StablecoinType.USDT,
            Amount: 102m,
            ToAddress: "THotWallet000000000000000000000000000",
            DepositIndex: 7,
            DepositAddress: "TDeposit000000000000000000000000000000");

        var result = await sut.BroadcastAsync(request, CancellationToken.None);

        Assert.Equal(TransferBroadcastStatus.Success, result.Status);
        Assert.Equal("tx-sweep-1", result.TxHash);
        Assert.NotNull(observed);
        Assert.EndsWith("/api/transfer/sweep", observed!.RequestUri!.ToString());
        Assert.NotNull(capturedBody);
        Assert.Contains("\"toHotWalletAddress\":\"THotWallet000000000000000000000000000\"", capturedBody);
        Assert.Contains("\"depositIndex\":7", capturedBody);
        Assert.Contains("TDeposit000000000000000000000000000000", capturedBody);
        Assert.DoesNotContain("toBuyerAddress", capturedBody);
    }

    [Fact]
    public async Task Sweep_Throws_When_DepositIndex_Missing()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { txHash = "tx" }),
            }));
        var sut = BuildClient(handler);

        var request = new TransferBroadcastRequest(
            BlockchainTransactionId: Guid.NewGuid(),
            Type: BlockchainTransactionType.SWEEP,
            Token: StablecoinType.USDT,
            Amount: 102m,
            ToAddress: "THotWallet000000000000000000000000000",
            DepositIndex: null,
            DepositAddress: "TDeposit000000000000000000000000000000");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.BroadcastAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task Refund_Throws_When_DepositIndex_Missing()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { txHash = "tx" }),
            }));
        var sut = BuildClient(handler);

        var request = new TransferBroadcastRequest(
            BlockchainTransactionId: Guid.NewGuid(),
            Type: BlockchainTransactionType.EXCESS_REFUND,
            Token: StablecoinType.USDT,
            Amount: 10m,
            ToAddress: "TBuyerSource000000000000000000000000",
            DepositIndex: null,
            DepositAddress: "TDeposit000000000000000000000000000000");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.BroadcastAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task BadRequest_ReturnsInvalidRequest_WithErrorCode()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new
                {
                    error = "INVALID_TRANSFER_AMOUNT",
                    message = "amount has too many decimals",
                    retryable = false,
                }),
            }));
        var sut = BuildClient(handler);

        var result = await sut.BroadcastAsync(
            new TransferBroadcastRequest(
                Guid.NewGuid(),
                BlockchainTransactionType.SELLER_PAYOUT,
                StablecoinType.USDT,
                10m,
                "TSeller00",
                null,
                null),
            CancellationToken.None);

        Assert.Equal(TransferBroadcastStatus.InvalidRequest, result.Status);
        Assert.Equal("INVALID_TRANSFER_AMOUNT", result.ErrorCode);
        Assert.Null(result.TxHash);
    }

    [Fact]
    public async Task BadGateway_ReturnsTransientFailure()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = JsonContent.Create(new
                {
                    error = "TRANSFER_BROADCAST_REJECTED",
                    message = "SIGERROR",
                    retryable = true,
                }),
            }));
        var sut = BuildClient(handler);

        var result = await sut.BroadcastAsync(
            new TransferBroadcastRequest(
                Guid.NewGuid(),
                BlockchainTransactionType.SELLER_PAYOUT,
                StablecoinType.USDT,
                10m,
                "TSeller00",
                null,
                null),
            CancellationToken.None);

        Assert.Equal(TransferBroadcastStatus.TransientFailure, result.Status);
        Assert.Equal("TRANSFER_BROADCAST_REJECTED", result.ErrorCode);
    }

    [Fact]
    public async Task NetworkException_ReturnsTransientFailure()
    {
        var handler = new RecordingHandler((_, _) =>
            throw new HttpRequestException("connection reset"));
        var sut = BuildClient(handler);

        var result = await sut.BroadcastAsync(
            new TransferBroadcastRequest(
                Guid.NewGuid(),
                BlockchainTransactionType.SELLER_PAYOUT,
                StablecoinType.USDT,
                10m,
                "TSeller",
                null,
                null),
            CancellationToken.None);

        Assert.Equal(TransferBroadcastStatus.TransientFailure, result.Status);
        Assert.Equal("TRANSPORT_FAILURE", result.ErrorCode);
    }

    [Fact]
    public async Task EmptyBody_OnHttp200_TreatedAsTransientFailure()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            }));
        var sut = BuildClient(handler);

        var result = await sut.BroadcastAsync(
            new TransferBroadcastRequest(
                Guid.NewGuid(),
                BlockchainTransactionType.SELLER_PAYOUT,
                StablecoinType.USDT,
                10m,
                "TSeller",
                null,
                null),
            CancellationToken.None);

        Assert.Equal(TransferBroadcastStatus.TransientFailure, result.Status);
        Assert.Equal("EMPTY_BODY", result.ErrorCode);
    }

    [Fact]
    public async Task GetStatus_Confirmed_When_Confirmations_GE_20_AndSuccess()
    {
        var handler = new RecordingHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.EndsWith("/api/transfer/status/tx-hash-001", req.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    txHash = "tx-hash-001",
                    blockNumber = 1_500_100L,
                    contractRet = "SUCCESS",
                    confirmations = 25,
                }),
            });
        });
        var sut = BuildClient(handler);

        var result = await sut.GetStatusAsync("tx-hash-001", CancellationToken.None);

        Assert.Equal(TransferStatusOutcome.Confirmed, result.Outcome);
        Assert.Equal(1_500_100L, result.BlockNumber);
        Assert.Equal(25, result.Confirmations);
    }

    [Fact]
    public async Task GetStatus_Failed_When_Confirmations_GE_20_AndContractReverted()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    txHash = "tx-hash-rev",
                    blockNumber = 1_500_100L,
                    contractRet = "REVERT",
                    confirmations = 22,
                }),
            }));
        var sut = BuildClient(handler);

        var result = await sut.GetStatusAsync("tx-hash-rev", CancellationToken.None);

        Assert.Equal(TransferStatusOutcome.Failed, result.Outcome);
        Assert.Equal("REVERT", result.ContractRet);
    }

    [Fact]
    public async Task GetStatus_Pending_When_Confirmations_LT_20()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    txHash = "tx-pending",
                    blockNumber = (long?)null,
                    contractRet = (string?)null,
                    confirmations = 5,
                }),
            }));
        var sut = BuildClient(handler);

        var result = await sut.GetStatusAsync("tx-pending", CancellationToken.None);

        Assert.Equal(TransferStatusOutcome.Pending, result.Outcome);
    }

    [Fact]
    public async Task GetStatus_Unavailable_On_Non200()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var sut = BuildClient(handler);

        var result = await sut.GetStatusAsync("tx-anything", CancellationToken.None);

        Assert.Equal(TransferStatusOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task InternalKey_Header_IsSet_OnBroadcast_AndStatus()
    {
        var headers = new List<string?>();
        var handler = new RecordingHandler((req, _) =>
        {
            headers.Add(req.Headers.TryGetValues("X-Internal-Key", out var values)
                ? string.Join(",", values)
                : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    txHash = "tx-key",
                    blockNumber = 1L,
                    contractRet = "SUCCESS",
                    confirmations = 20,
                }),
            });
        });
        var sut = BuildClient(handler, internalKey: "super-secret-shared");

        await sut.BroadcastAsync(
            new TransferBroadcastRequest(
                Guid.NewGuid(),
                BlockchainTransactionType.SELLER_PAYOUT,
                StablecoinType.USDT,
                1m,
                "TX",
                null,
                null),
            CancellationToken.None);
        await sut.GetStatusAsync("tx-key", CancellationToken.None);

        Assert.Equal(2, headers.Count);
        Assert.All(headers, h => Assert.Equal("super-secret-shared", h));
    }

    private static HttpBlockchainTransferClient BuildClient(
        HttpMessageHandler handler, string internalKey = "")
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(SidecarBaseUrl),
        };
        var options = Options.Create(new BlockchainSidecarOptions
        {
            BaseUrl = SidecarBaseUrl,
            InternalKey = internalKey,
            TimeoutSeconds = 5,
        });
        return new HttpBlockchainTransferClient(http, options, NullLogger<HttpBlockchainTransferClient>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responder(request, cancellationToken);
    }
}
