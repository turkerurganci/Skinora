using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.Transactions.Tests.Contract;

/// <summary>
/// T158 — the backend half of the Ethereum contract additions, replayed
/// against the example bodies the service's own tests use
/// (<c>sidecar-ethereum/contracts/</c>, "süreç sınırını geçen değerde iki
/// tarafın testini aynı örnek dosyaya bağla"):
/// <list type="bullet">
///   <item>(h) the derive answer's <c>watchFromBlock</c> reaches the allocator
///     as the same block; an Ethereum answer without it is not accepted; the
///     monitor start body carries it as the example shows;</item>
///   <item>(f) the hot-wallet refund body is byte-for-byte the example the
///     service accepts;</item>
///   <item>(g) the status read carries the row's expectation on Ethereum only,
///     and the <c>UNKNOWN</c> example is <see cref="TransferStatusOutcome.Unverified"/>
///     — never Confirmed, never Failed.</item>
/// </list>
/// </summary>
public sealed class EthereumT158ContractTests
{
    private const string BaseUrl = "http://ethereum-sidecar.test/";

    // ---- (h) watchFromBlock -------------------------------------------------

    [Fact]
    public async Task Derive_Example_Answer_Delivers_The_Start_Block()
    {
        var body = LoadExample("wallet-derive", "response.json");
        var client = SidecarClient(BlockchainNetwork.ETHEREUM, (_, _) => Task.FromResult(Json(body.ToJsonString())));

        var result = await client.DeriveAddressAsync(0, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.Success, result.Status);
        Assert.Equal(long.Parse(Text(body, "watchFromBlock")), result.WatchFromBlock);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"\"")]
    [InlineData("\"-1\"")]
    [InlineData("\"0x10\"")]
    [InlineData("\"12.5\"")]
    [InlineData("true")]
    public async Task Ethereum_Derive_Without_A_Usable_Start_Block_Is_Not_Accepted(string? raw)
    {
        var body = LoadExample("wallet-derive", "response.json").AsObject();
        body.Remove("watchFromBlock");
        if (raw is not null) body["watchFromBlock"] = JsonNode.Parse(raw);
        var client = SidecarClient(BlockchainNetwork.ETHEREUM, (_, _) => Task.FromResult(Json(body.ToJsonString())));

        var result = await client.DeriveAddressAsync(0, Guid.NewGuid(), CancellationToken.None);

        // Unavailable: the allocator retries next run; no address is persisted without its start block.
        Assert.Equal(BlockchainSidecarStatus.Unavailable, result.Status);
        Assert.Null(result.Address);
    }

    [Fact]
    public async Task Ethereum_Derive_Accepts_A_Json_Number_Start_Block()
    {
        var body = LoadExample("wallet-derive", "response.json").AsObject();
        body["watchFromBlock"] = 26_214_908;
        var client = SidecarClient(BlockchainNetwork.ETHEREUM, (_, _) => Task.FromResult(Json(body.ToJsonString())));

        var result = await client.DeriveAddressAsync(0, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(26_214_908L, result.WatchFromBlock);
    }

    [Fact]
    public async Task Tron_Derive_Needs_No_Start_Block_And_Reports_None()
    {
        const string tron = """{"address":"TD5gsCwxykWsLN9aPrq2TAfNjByuZKYp4E","derivationPath":"m/44'/195'/0'/0/0","index":0,"watchFromBlock":"77"}""";
        var client = SidecarClient(BlockchainNetwork.TRON, (_, _) => Task.FromResult(Json(tron)));

        var result = await client.DeriveAddressAsync(0, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.Success, result.Status);
        Assert.Null(result.WatchFromBlock);
    }

    [Fact]
    public async Task Monitor_Start_Body_Is_The_Example_The_Service_Arms_From()
    {
        var expected = LoadExample("monitor", "start.request.json");
        var sent = new List<string>();
        var client = SidecarClient(BlockchainNetwork.ETHEREUM, async (request, ct) =>
        {
            sent.Add(await request.Content!.ReadAsStringAsync(ct));
            return Json("""{"acknowledged":true,"started":true,"cadence":"HOLDING"}""");
        });

        await client.StartMonitoringAsync(
            new PaymentMonitorStartRequest(
                Address: Text(expected, "address"),
                PaymentAddressId: Guid.Parse(Text(expected, "paymentAddressId")),
                TransactionId: Guid.Parse(Text(expected, "transactionId")),
                ExpectedContract: Text(expected, "expectedContract"),
                ExpectedSymbol: Text(expected, "expectedSymbol"),
                Cadence: PaymentMonitorCadence.Holding,
                WatchFromBlock: long.Parse(Text(expected, "watchFromBlock"))),
            CancellationToken.None);

        AssertSameJson(expected, Assert.Single(sent));
    }

    [Fact]
    public async Task Monitor_Start_Body_Without_A_Start_Block_Is_Unchanged_No_Null_Field()
    {
        var sent = new List<string>();
        var client = SidecarClient(BlockchainNetwork.TRON, async (request, ct) =>
        {
            sent.Add(await request.Content!.ReadAsStringAsync(ct));
            return Json("""{"acknowledged":true,"started":true,"cadence":"PAYMENT"}""");
        });

        await client.StartMonitoringAsync(
            new PaymentMonitorStartRequest(
                "TD5gsCwxykWsLN9aPrq2TAfNjByuZKYp4E", Guid.NewGuid(), Guid.NewGuid(),
                "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", "USDT", PaymentMonitorCadence.Payment),
            CancellationToken.None);
        await client.StartPostCancelMonitoringAsync(
            new PostCancelMonitorStartRequest(
                "TD5gsCwxykWsLN9aPrq2TAfNjByuZKYp4E", Guid.NewGuid(), Guid.NewGuid(),
                "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", "USDT", DateTime.UtcNow),
            CancellationToken.None);

        Assert.All(sent, body => Assert.DoesNotContain("watchFromBlock", body));
    }

    [Fact]
    public async Task Post_Cancel_Start_Carries_The_Start_Block()
    {
        var sent = new List<string>();
        var client = SidecarClient(BlockchainNetwork.ETHEREUM, async (request, ct) =>
        {
            sent.Add(await request.Content!.ReadAsStringAsync(ct));
            return Json("""{"acknowledged":true,"started":true,"state":"POST_CANCEL_24H"}""");
        });

        await client.StartPostCancelMonitoringAsync(
            new PostCancelMonitorStartRequest(
                "0x9858effd232b4033e47d90003d41ec34ecaeda94", Guid.NewGuid(), Guid.NewGuid(),
                "0xdac17f958d2ee523a2206206994597c13d831ec7", "USDT", DateTime.UtcNow,
                WatchFromBlock: 26_214_908L),
            CancellationToken.None);

        Assert.Equal("26214908", JsonNode.Parse(Assert.Single(sent))!["watchFromBlock"]!.GetValue<string>());
    }

    // ---- (f) hot-wallet refund ------------------------------------------------

    [Fact]
    public async Task Hot_Wallet_Refund_Body_Is_The_Example_The_Service_Accepts()
    {
        var expected = LoadExample("transfer-refund", "hot-wallet.request.json");
        var sent = new List<(string Path, string Body)>();
        var client = TransferClient(BlockchainNetwork.ETHEREUM, async (request, ct) =>
        {
            sent.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(ct)));
            return Json("""{"txHash":"0x4f8e3b1c2d5a6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f7"}""");
        });

        await client.BroadcastAsync(
            new TransferBroadcastRequest(
                BlockchainTransactionId: Guid.Parse(Text(expected, "blockchainTransactionId")),
                Type: BlockchainTransactionType.BUYER_REFUND,
                Token: StablecoinType.USDT,
                Amount: decimal.Parse(Text(expected, "amount"), System.Globalization.CultureInfo.InvariantCulture),
                ToAddress: Text(expected, "toBuyerAddress"),
                DepositIndex: null,
                DepositAddress: null,
                FeeCapUsd: decimal.Parse(Text(expected, "feeCapUsd"), System.Globalization.CultureInfo.InvariantCulture),
                FromHotWallet: true),
            CancellationToken.None);

        var (path, body) = Assert.Single(sent);
        Assert.Equal("/api/transfer/refund", path);
        AssertSameJson(expected, body);
    }

    // ---- (g) status expectation + UNKNOWN ---------------------------------

    private static readonly TransferStatusExpectation Expectation = new(
        "0xdac17f958d2ee523a2206206994597c13d831ec7",
        "0x7070707070707070707070707070707070707070",
        "0x2222222222222222222222222222222222222222",
        20.4m);

    [Fact]
    public async Task Ethereum_Status_Read_Carries_The_Row_Expectation()
    {
        var asked = new List<Uri>();
        var client = TransferClient(BlockchainNetwork.ETHEREUM, (request, _) =>
        {
            asked.Add(request.RequestUri!);
            return Task.FromResult(Json(LoadExample("transfer-status", "confirmed.response.json").ToJsonString()));
        });

        await ((IBlockchainTransferClient)client).GetStatusAsync("0xabc", Expectation, CancellationToken.None);

        var query = System.Web.HttpUtility.ParseQueryString(Assert.Single(asked).Query);
        Assert.Equal(Expectation.Contract, query["contract"]);
        Assert.Equal(Expectation.FromAddress, query["from"]);
        Assert.Equal(Expectation.ToAddress, query["to"]);
        Assert.Equal("20.4", query["amount"]);
    }

    [Fact]
    public async Task Tron_Status_Read_Is_Unchanged_No_Query()
    {
        var asked = new List<Uri>();
        var client = TransferClient(BlockchainNetwork.TRON, (request, _) =>
        {
            asked.Add(request.RequestUri!);
            return Task.FromResult(Json("""{"txHash":"abc","blockNumber":1,"contractRet":"SUCCESS","confirmations":25}"""));
        });

        await ((IBlockchainTransferClient)client).GetStatusAsync("abc", Expectation, CancellationToken.None);

        Assert.Equal(string.Empty, Assert.Single(asked).Query);
    }

    [Fact]
    public async Task Unknown_Example_Is_Unverified_Never_Confirmed_Never_Failed()
    {
        var body = LoadExample("transfer-status", "unknown.response.json");
        var client = TransferClient(BlockchainNetwork.ETHEREUM, (_, _) => Task.FromResult(Json(body.ToJsonString())));

        var status = await client.GetStatusAsync(Text(body, "txHash"), Expectation, CancellationToken.None);

        Assert.Equal(TransferStatusOutcome.Unverified, status.Outcome);
    }

    // ---- helpers --------------------------------------------------------------

    private static HttpBlockchainSidecarClient SidecarClient(
        BlockchainNetwork network,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        new(
            network,
            new HttpClient(new Handler(respond)) { BaseAddress = new Uri(BaseUrl) },
            new BlockchainSidecarOptions { BaseUrl = BaseUrl },
            NullLogger<HttpBlockchainSidecarClient>.Instance);

    private static HttpBlockchainTransferClient TransferClient(
        BlockchainNetwork network,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        new(
            network,
            new HttpClient(new Handler(respond)) { BaseAddress = new Uri(BaseUrl) },
            new BlockchainSidecarOptions { BaseUrl = BaseUrl },
            NullLogger<HttpBlockchainTransferClient>.Instance);

    private static void AssertSameJson(JsonNode expected, string actualBody)
    {
        var actual = JsonNode.Parse(actualBody);
        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"expected {expected.ToJsonString()}{Environment.NewLine}sent     {actual?.ToJsonString()}");
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static JsonNode LoadExample(string folder, string file)
    {
        var path = Path.Combine(FindRepositoryRoot(), "sidecar-ethereum", "contracts", folder, file);
        return JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{path} is empty.");
    }

    private static string Text(JsonNode body, string key) =>
        body[key]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Example body has no '{key}'.");

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

    private sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
