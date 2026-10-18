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

    // -------------------- T148 — finality is the network's policy --------------------

    [Theory]
    // Ethereum (11 §F8 K5): only an explicit `finalized` counts — a block count
    // far past TRON's 20 does not.
    [InlineData(BlockchainNetwork.ETHEREUM, 120, false, TransferStatusOutcome.Pending)]
    [InlineData(BlockchainNetwork.ETHEREUM, 120, null, TransferStatusOutcome.Pending)]
    [InlineData(BlockchainNetwork.ETHEREUM, 3, true, TransferStatusOutcome.Confirmed)]
    // TRON counts blocks and ignores a `finalized` flag either way.
    [InlineData(BlockchainNetwork.TRON, 20, false, TransferStatusOutcome.Confirmed)]
    [InlineData(BlockchainNetwork.TRON, 19, true, TransferStatusOutcome.Pending)]
    public async Task GetStatus_Applies_The_Networks_Finality_Rule(
        BlockchainNetwork network, int confirmations, bool? finalized, TransferStatusOutcome expected)
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    txHash = "tx-policy",
                    blockNumber = 1_000L,
                    contractRet = "SUCCESS",
                    confirmations,
                    finalized,
                    realizedFeeSun = 6_400_000L,
                    energyUsageTotal = 64_285L,
                    originEnergyUsage = 0L,
                }),
            }));
        var sut = BuildClient(handler, network: network);

        var result = await sut.GetStatusAsync("tx-policy", CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task GetStatus_Drops_Tron_Resource_Fields_On_Ethereum()
    {
        // SUN and Energy are TRON's receipt vocabulary; an Ethereum answer that
        // happened to carry them must not be recorded as such (T148).
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    txHash = "0xhash",
                    blockNumber = 1_000L,
                    contractRet = "SUCCESS",
                    confirmations = 64,
                    finalized = true,
                    realizedFeeSun = 6_400_000L,
                    energyUsageTotal = 64_285L,
                    originEnergyUsage = 0L,
                }),
            }));

        var eth = await BuildClient(handler, network: BlockchainNetwork.ETHEREUM)
            .GetStatusAsync("0xhash", CancellationToken.None);
        var tron = await BuildClient(handler, network: BlockchainNetwork.TRON)
            .GetStatusAsync("0xhash", CancellationToken.None);

        Assert.Equal(TransferStatusOutcome.Confirmed, eth.Outcome);
        Assert.Null(eth.RealizedFeeSun);
        Assert.Null(eth.EnergyUsageTotal);
        Assert.Null(eth.OriginEnergyUsage);
        Assert.Equal(6_400_000L, tron.RealizedFeeSun);
        Assert.Equal(64_285L, tron.EnergyUsageTotal);
    }

    [Theory]
    [InlineData(BlockchainNetwork.TRON, "TRON")]
    [InlineData(BlockchainNetwork.ETHEREUM, "ETHEREUM")]
    public async Task Broadcast_Body_Names_The_Clients_Network(BlockchainNetwork network, string wire)
    {
        string? body = null;
        var handler = new RecordingHandler(async (req, _) =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { txHash = "tx" }),
            };
        });

        await BuildClient(handler, network: network).BroadcastAsync(
            new TransferBroadcastRequest(
                Guid.NewGuid(), BlockchainTransactionType.SELLER_PAYOUT, StablecoinType.USDT,
                1m, "TSeller000000000000000000000000000000", null, null),
            CancellationToken.None);

        Assert.Contains($"\"network\":\"{wire}\"", body);
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
    public async Task GetStatus_KeepsTheChainReadBudget_OnAClientSizedForBroadcasts()
    {
        // The named client's timeout is the broadcast budget (300 s — it waits
        // for blocks). A status read must still give up on its own, shorter
        // budget: TimeoutSeconds 1 → 3 s. Without that, the handler below
        // answers after 20 s and the stuck read looks like a confirmation.
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    txHash = "tx-slow",
                    blockNumber = 1L,
                    contractRet = "SUCCESS",
                    confirmations = 25,
                }),
            };
        });
        var sut = BuildClient(handler, timeoutSeconds: 1);

        var result = await sut.GetStatusAsync("tx-slow", CancellationToken.None);

        Assert.Equal(TransferStatusOutcome.Unavailable, result.Outcome);
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

    // -------------------- T152 — Ethereum sends: cap, refund type, deferral, row-specific 4xx --------------------

    [Theory]
    [InlineData(BlockchainTransactionType.SELLER_PAYOUT, "api/transfer/payout")]
    [InlineData(BlockchainTransactionType.SWEEP, "api/transfer/sweep")]
    public async Task Fee_Cap_Travels_In_The_Body_When_Set_And_Is_Omitted_Otherwise(
        BlockchainTransactionType type, string path)
    {
        var bodies = new List<string>();
        var handler = new RecordingHandler(async (req, _) =>
        {
            Assert.EndsWith(path, req.RequestUri!.ToString());
            bodies.Add(await req.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { txHash = "0xa" }) };
        });
        var sut = BuildClient(handler, network: BlockchainNetwork.ETHEREUM);
        TransferBroadcastRequest Request(decimal? cap) => new(
            Guid.NewGuid(), type, StablecoinType.USDT, 10m, "0x1111111111111111111111111111111111111111",
            type == BlockchainTransactionType.SWEEP ? 3 : null,
            type == BlockchainTransactionType.SWEEP ? "0x2222222222222222222222222222222222222222" : null,
            cap);

        await sut.BroadcastAsync(Request(0.5m), CancellationToken.None);
        await sut.BroadcastAsync(Request(null), CancellationToken.None);

        Assert.Contains("\"feeCapUsd\":\"0.5\"", bodies[0]);
        Assert.DoesNotContain("feeCapUsd", bodies[1]);
    }

    [Fact]
    public async Task Refund_Body_Names_Its_Refund_Type()
    {
        string? body = null;
        var handler = new RecordingHandler(async (req, _) =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { txHash = "0xa" }) };
        });

        await BuildClient(handler, network: BlockchainNetwork.ETHEREUM).BroadcastAsync(
            new TransferBroadcastRequest(
                Guid.NewGuid(), BlockchainTransactionType.WRONG_TOKEN_REFUND, StablecoinType.USDC, 7m,
                "0x1111111111111111111111111111111111111111", 3, "0x2222222222222222222222222222222222222222"),
            CancellationToken.None);

        Assert.Contains("\"refundType\":\"WRONG_TOKEN_REFUND\"", body);
    }

    [Fact]
    public async Task A_Deferred_Answer_Is_Deferred_Not_A_Transient_Failure()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent.Create(new
                {
                    error = "FEE_ABOVE_CAP",
                    message = "above cap",
                    deferred = true,
                    retryable = true,
                    waitingSince = "2026-10-18T09:00:00.000Z",
                }),
            }));

        var result = await BuildClient(handler, network: BlockchainNetwork.ETHEREUM).BroadcastAsync(
            new TransferBroadcastRequest(
                Guid.NewGuid(), BlockchainTransactionType.SELLER_PAYOUT, StablecoinType.USDT, 1m,
                "0x1111111111111111111111111111111111111111", null, null, 0.5m),
            CancellationToken.None);

        Assert.Equal(TransferBroadcastStatus.Deferred, result.Status);
        Assert.Equal("FEE_ABOVE_CAP", result.ErrorCode);
        Assert.Contains("2026-10-18T09:00:00", result.ErrorMessage);
    }

    [Fact]
    public async Task A_503_Without_The_Deferred_Flag_Stays_A_Transient_Failure()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent.Create(new { error = "NOT_IMPLEMENTED", retryable = true }),
            }));

        var result = await BuildClient(handler, network: BlockchainNetwork.ETHEREUM).BroadcastAsync(
            new TransferBroadcastRequest(
                Guid.NewGuid(), BlockchainTransactionType.SELLER_PAYOUT, StablecoinType.USDT, 1m,
                "0x1111111111111111111111111111111111111111", null, null),
            CancellationToken.None);

        Assert.Equal(TransferBroadcastStatus.TransientFailure, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, TransferStatusOutcome.Rejected)]
    [InlineData(HttpStatusCode.NotFound, TransferStatusOutcome.Rejected)]
    [InlineData(HttpStatusCode.Conflict, TransferStatusOutcome.Rejected)]
    [InlineData(HttpStatusCode.Unauthorized, TransferStatusOutcome.Unavailable)]
    [InlineData(HttpStatusCode.Forbidden, TransferStatusOutcome.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, TransferStatusOutcome.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, TransferStatusOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, TransferStatusOutcome.Unavailable)]
    public async Task GetStatus_A_Row_Specific_4xx_Is_Rejected_The_Rest_Unavailable(
        HttpStatusCode code, TransferStatusOutcome expected)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(code)));

        var result = await BuildClient(handler, network: BlockchainNetwork.ETHEREUM)
            .GetStatusAsync("0xabc", CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task GetStatus_Passes_On_The_Hash_The_Service_Reports()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    txHash = "0xreplacement",
                    blockNumber = 10L,
                    contractRet = "SUCCESS",
                    confirmations = 2,
                    finalized = true,
                }),
            }));

        var result = await BuildClient(handler, network: BlockchainNetwork.ETHEREUM)
            .GetStatusAsync("0xoriginal", CancellationToken.None);

        Assert.Equal(TransferStatusOutcome.Confirmed, result.Outcome);
        Assert.Equal("0xreplacement", result.TxHash);
    }

    private static HttpBlockchainTransferClient BuildClient(
        HttpMessageHandler handler, string internalKey = "", int timeoutSeconds = 5,
        BlockchainNetwork network = BlockchainNetwork.TRON)
    {
        var sidecarOptions = new BlockchainSidecarOptions
        {
            BaseUrl = SidecarBaseUrl,
            InternalKey = internalKey,
            TimeoutSeconds = timeoutSeconds,
        };
        // Wired as TransactionsModule wires it: the client timeout is the
        // broadcast budget.
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(SidecarBaseUrl),
            Timeout = sidecarOptions.ResolveTransferTimeout(),
        };
        return new HttpBlockchainTransferClient(
            network, http, sidecarOptions, NullLogger<HttpBlockchainTransferClient>.Instance);
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
