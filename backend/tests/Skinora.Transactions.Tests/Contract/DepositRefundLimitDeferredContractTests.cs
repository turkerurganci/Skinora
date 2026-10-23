using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.Transactions.Tests.Contract;

/// <summary>
/// T172b (proje sahibi kararı 2026-10-23, ⚠ PARA) — the backend half of the
/// signing services' "deposit refund cap reached" answer, replayed against the
/// example bodies the services' own handler tests emit
/// (<c>sidecar-{blockchain,ethereum}/contracts/transfer-refund/deferred.response.json</c>).
/// </summary>
/// <remarks>
/// The answer must become <see cref="TransferBroadcastStatus.Deferred"/>: the
/// dispatcher then keeps the row PENDING without spending a retry and asks
/// again. Read as a refusal (400 → FAILED) the refund would invite a manual
/// re-open under a new id; read as transient on TRON it would burn the retry
/// budget and turn FAILED all the same.
/// </remarks>
public sealed class DepositRefundLimitDeferredContractTests
{
    private const string BaseUrl = "http://blockchain-sidecar.test/";

    [Theory]
    [InlineData(BlockchainNetwork.TRON, "sidecar-blockchain")]
    [InlineData(BlockchainNetwork.ETHEREUM, "sidecar-ethereum")]
    public async Task Signer_Deposit_Refund_Cap_Answer_Is_A_Wait(BlockchainNetwork network, string service)
    {
        var body = LoadExample(service);

        var result = await Broadcast(network, HttpStatusCode.ServiceUnavailable, body.ToJsonString());

        Assert.Equal(TransferBroadcastStatus.Deferred, result.Status);
        Assert.Null(result.TxHash);
        Assert.Equal("DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED", result.ErrorCode);
        Assert.Contains(body["message"]!.GetValue<string>(), result.ErrorMessage);
    }

    [Theory]
    [InlineData("DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED")]
    [InlineData("DEPOSIT_REFUND_LIMIT_NOT_CONFIGURED")]
    [InlineData("DEPOSIT_REFUND_LEDGER_UNAVAILABLE")]
    public async Task Cap_Code_Waits_Even_Without_The_Deferred_Flag(string code)
    {
        // A drifted service answering the cap as a plain 400 must still not
        // turn the refund FAILED.
        var result = await Broadcast(
            BlockchainNetwork.TRON,
            HttpStatusCode.BadRequest,
            $$"""{"error":"{{code}}","message":"cap","retryable":false}""");

        Assert.Equal(TransferBroadcastStatus.Deferred, result.Status);
        Assert.Equal(code, result.ErrorCode);
    }

    [Fact]
    public async Task Other_Refusals_Stay_Refusals()
    {
        var result = await Broadcast(
            BlockchainNetwork.TRON,
            HttpStatusCode.BadRequest,
            """{"error":"TRANSFER_AMOUNT_ABOVE_LIMIT","message":"above","retryable":false}""");

        Assert.Equal(TransferBroadcastStatus.InvalidRequest, result.Status);
    }

    private static Task<TransferBroadcastResult> Broadcast(
        BlockchainNetwork network, HttpStatusCode status, string body)
    {
        var client = new HttpBlockchainTransferClient(
            network,
            new HttpClient(new Handler(status, body)) { BaseAddress = new Uri(BaseUrl) },
            new BlockchainSidecarOptions { BaseUrl = BaseUrl },
            NullLogger<HttpBlockchainTransferClient>.Instance);

        return client.BroadcastAsync(
            new TransferBroadcastRequest(
                Guid.NewGuid(), BlockchainTransactionType.BUYER_REFUND, StablecoinType.USDT, 50m,
                "TBuyer", 7, "TDeposit"),
            CancellationToken.None);
    }

    private static JsonNode LoadExample(string service)
    {
        var path = Path.Combine(
            FindRepositoryRoot(), service, "contracts", "transfer-refund", "deferred.response.json");
        return JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{path} is empty.");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repository root not found (looked for docker-compose.yml).");
    }

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
