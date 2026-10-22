using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.Transactions.Tests.Contract;

/// <summary>
/// T160c kapanış (S4-1, ⚠ PARA) — the backend half of the TRON service's
/// deposit-send answer (<c>POST /api/transfer/sweep</c>, <c>/api/transfer/refund</c>),
/// replayed against the example bodies the service's own handler test emits
/// (<c>sidecar-blockchain/contracts/transfer-{sweep,refund}/response.json</c>).
/// </summary>
/// <remarks>
/// <c>platformSpendSun</c> is the SUN the send cost the hot wallet; the
/// dispatch job writes it to <c>PlatformNetworkCost</c> and AD38 books it as
/// the platform's expense. A dropped or renamed field never fails the send —
/// the row only turns "not measured" — so without this pair nothing would
/// notice (#327: both halves must replay the same bytes).
/// </remarks>
public sealed class BlockchainSidecarTransferResponseContractTests
{
    private const string BaseUrl = "http://blockchain-sidecar.test/";

    [Theory]
    [InlineData("transfer-sweep", BlockchainTransactionType.SWEEP)]
    [InlineData("transfer-refund", BlockchainTransactionType.BUYER_REFUND)]
    [InlineData("transfer-sweep", BlockchainTransactionType.REFUND_FEE_SWEEP)]
    public async Task Tron_Deposit_Send_Example_Delivers_The_Platform_Spend_Exactly(
        string folder, BlockchainTransactionType type)
    {
        var body = LoadExample(folder);
        var client = new HttpBlockchainTransferClient(
            BlockchainNetwork.TRON,
            new HttpClient(new Handler(body.ToJsonString())) { BaseAddress = new Uri(BaseUrl) },
            new BlockchainSidecarOptions { BaseUrl = BaseUrl },
            NullLogger<HttpBlockchainTransferClient>.Instance);

        var result = await client.BroadcastAsync(
            new TransferBroadcastRequest(
                Guid.NewGuid(), type, StablecoinType.USDT, 10m, "TTarget", 7, "TDeposit"),
            CancellationToken.None);

        Assert.Equal(TransferBroadcastStatus.Success, result.Status);
        Assert.Equal(body["txHash"]!.GetValue<string>(), result.TxHash);
        Assert.Equal(body["platformSpendSun"]!.GetValue<long>(), result.PlatformSpendSun);
        Assert.NotEqual(body["fallbackAmountSun"]!.GetValue<long>(), result.PlatformSpendSun);
    }

    private static JsonNode LoadExample(string folder)
    {
        var path = Path.Combine(FindRepositoryRoot(), "sidecar-blockchain", "contracts", folder, "response.json");
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

    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
