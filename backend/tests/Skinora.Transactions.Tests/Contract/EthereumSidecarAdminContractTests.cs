using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.Transactions.Tests.Contract;

/// <summary>
/// T157 — the backend half of three Ethereum-service calls the admin wallet
/// screen, AD39 and the outbound-row closure tool use, against the example files
/// the service's own handler tests replay
/// (<c>sidecar-ethereum/contracts/cold-wallet/request.json</c>,
/// <c>contracts/platform-wallets/response.json</c>,
/// <c>contracts/transfer-verify/{request,response}.json</c>).
/// </summary>
public sealed class EthereumSidecarAdminContractTests
{
    [Fact]
    public async Task ColdWalletRequest_IsTheBodyTheEthereumServiceAccepts()
    {
        var expected = Load("cold-wallet", "request.json");
        var sent = new List<(string Path, string Body)>();
        var client = Client(BlockchainNetwork.ETHEREUM, async (request, ct) =>
        {
            sent.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { txHash = "0x" + new string('c', 64), nonce = 1, replayed = false }),
            };
        });

        var result = await client.SendHotToColdTransferAsync(
            new HotToColdTransferRequest(
                ColdTransferId: Guid.Parse(expected["coldTransferId"]!.GetValue<string>()),
                ToColdAddress: expected["toColdAddress"]!.GetValue<string>(),
                Amount: expected["amount"]!.GetValue<string>(),
                Token: expected["token"]!.GetValue<string>(),
                FeeCapUsd: expected["feeCapUsd"]!.GetValue<string>()),
            CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.Success, result.Status);
        var (path, body) = Assert.Single(sent);
        Assert.Equal("/api/transfer/cold-wallet", path);
        Assert.True(
            JsonNode.DeepEquals(expected, JsonNode.Parse(body)),
            "The backend no longer sends the body the Ethereum service's handler test accepts."
            + Environment.NewLine + "  expected: " + expected.ToJsonString()
            + Environment.NewLine + "  sent:     " + body);
    }

    [Fact]
    public async Task ColdWalletRequest_WithoutAFeeCap_SendsNoFeeCapKey()
    {
        // TRON's service has no fee cap; a null must not become "feeCapUsd": null.
        string? sentBody = null;
        var client = Client(BlockchainNetwork.TRON, async (request, ct) =>
        {
            sentBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { txHash = "tron-tx" }),
            };
        });

        await client.SendHotToColdTransferAsync(
            new HotToColdTransferRequest(Guid.NewGuid(), "TCold", "10", "USDT"), CancellationToken.None);

        Assert.DoesNotContain("feeCapUsd", sentBody);
    }

    [Theory]
    [InlineData("{\"error\":\"FEE_ABOVE_CAP\",\"message\":\"x\",\"deferred\":true,\"retryable\":true}", BlockchainSidecarStatus.Deferred, "FEE_ABOVE_CAP")]
    [InlineData("{\"error\":\"ETH_PRICE_UNAVAILABLE\",\"deferred\":true}", BlockchainSidecarStatus.Deferred, "ETH_PRICE_UNAVAILABLE")]
    [InlineData("{\"error\":\"JOURNAL_BEHIND_CHAIN\",\"message\":\"x\"}", BlockchainSidecarStatus.Deferred, "JOURNAL_BEHIND_CHAIN")]
    [InlineData("{\"error\":\"HD_WALLET_NOT_CONFIGURED\"}", BlockchainSidecarStatus.NotConfigured, null)]
    [InlineData("not json", BlockchainSidecarStatus.NotConfigured, null)]
    [InlineData("", BlockchainSidecarStatus.NotConfigured, null)]
    public async Task ColdWallet_503_IsDeferredOnlyWhenTheServiceSaysNothingWasSignedForNow(
        string body, BlockchainSidecarStatus expected, string? reason)
    {
        var client = Client(BlockchainNetwork.ETHEREUM, (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            }));

        var result = await client.SendHotToColdTransferAsync(
            new HotToColdTransferRequest(Guid.NewGuid(), "0xcold", "10", "USDT", "0.5"),
            CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Equal(reason, result.Reason);
        Assert.Null(result.TxHash);
    }

    [Fact]
    public async Task PlatformWallets_ReadsTheContractResponse_AndAsksTheServiceByNetwork()
    {
        var response = Load("platform-wallets", "response.json");
        var sent = new List<(string Path, string Body)>();
        var client = Client(BlockchainNetwork.ETHEREUM, async (request, ct) =>
        {
            sent.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) };
        });

        var result = await client.GetPlatformWalletsAsync(CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.Success, result.Status);
        Assert.Equal(response["hotWalletAddress"]!.GetValue<string>(), result.HotWalletAddress);
        Assert.Equal(response["fuelWalletAddress"]!.GetValue<string>(), result.FuelWalletAddress);
        Assert.Equal(response["coldWalletAddress"]!.GetValue<string>(), result.ColdWalletAddress);
        var (path, body) = Assert.Single(sent);
        Assert.Equal("/api/wallet/platform", path);
        Assert.Equal("ETHEREUM", JsonNode.Parse(body)!["network"]!.GetValue<string>());
    }

    [Fact]
    public async Task PlatformWallets_TronIsNotAsked()
    {
        var calls = 0;
        var client = Client(BlockchainNetwork.TRON, (_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var result = await client.GetPlatformWalletsAsync(CancellationToken.None);

        Assert.Equal(0, calls);
        Assert.Equal(BlockchainSidecarStatus.NotConfigured, result.Status);
    }

    [Fact]
    public async Task PlatformWallets_AServiceError_IsUnavailable_NeverAnEmptyList()
    {
        var client = Client(BlockchainNetwork.ETHEREUM, (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await client.GetPlatformWalletsAsync(CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.Unavailable, result.Status);
        Assert.Null(result.FuelWalletAddress);
    }

    // ---- POST /api/transfer/verify (the closure tool's recipient/amount check) ----

    [Fact]
    public async Task VerifyRequest_IsTheBodyTheEthereumServiceAccepts_AndItsAnswerIsRead()
    {
        var expected = Load("transfer-verify", "request.json");
        var answer = Load("transfer-verify", "response.json");
        var sent = new List<(string Path, string Body)>();
        var client = TransferClient(BlockchainNetwork.ETHEREUM, async (request, ct) =>
        {
            sent.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(answer) };
        });

        var result = await client.VerifyTransferAsync(
            new TransferVerifyRequest(
                expected["txHash"]!.GetValue<string>(),
                Enum.Parse<StablecoinType>(expected["token"]!.GetValue<string>()),
                expected["from"]!.GetValue<string>(),
                expected["to"]!.GetValue<string>(),
                decimal.Parse(expected["amount"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)),
            CancellationToken.None);

        Assert.Equal(TransferVerifyOutcome.Match, result.Outcome);
        Assert.Equal(answer["blockNumber"]!.GetValue<long>(), result.BlockNumber);
        var (path, body) = Assert.Single(sent);
        Assert.Equal("/api/transfer/verify", path);
        Assert.True(
            JsonNode.DeepEquals(expected, JsonNode.Parse(body)),
            "The backend no longer sends the body the Ethereum service's verify handler test accepts."
            + Environment.NewLine + "  expected: " + expected.ToJsonString()
            + Environment.NewLine + "  sent:     " + body);
    }

    [Fact]
    public async Task VerifyRequest_WithoutASender_SendsNoFromKey()
    {
        string? sent = null;
        var client = TransferClient(BlockchainNetwork.ETHEREUM, async (request, ct) =>
        {
            sent = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { outcome = "MATCH", matches = true, blockNumber = 1 }),
            };
        });

        await client.VerifyTransferAsync(
            new TransferVerifyRequest("0x" + new string('a', 64), StablecoinType.USDT, null, "0xto", 1.5m),
            CancellationToken.None);

        Assert.DoesNotContain("\"from\"", sent);
        Assert.Contains("\"amount\":\"1.5\"", sent);
    }

    [Theory]
    [InlineData("{\"outcome\":\"MATCH\",\"matches\":true,\"blockNumber\":5}", TransferVerifyOutcome.Match)]
    [InlineData("{\"outcome\":\"NO_MATCHING_TRANSFER\",\"matches\":false,\"blockNumber\":5}", TransferVerifyOutcome.NoMatchingTransfer)]
    [InlineData("{\"outcome\":\"REVERTED\",\"matches\":false,\"blockNumber\":5}", TransferVerifyOutcome.Reverted)]
    [InlineData("{\"outcome\":\"NOT_FOUND\",\"matches\":false,\"blockNumber\":null}", TransferVerifyOutcome.NotFound)]
    // A service that contradicts itself, an unknown outcome, a missing flag or garbage is NOT a pass.
    [InlineData("{\"outcome\":\"MATCH\",\"matches\":false}", TransferVerifyOutcome.Unavailable)]
    [InlineData("{\"outcome\":\"MATCH\"}", TransferVerifyOutcome.Unavailable)]
    [InlineData("{\"outcome\":\"NO_MATCHING_TRANSFER\",\"matches\":true}", TransferVerifyOutcome.Unavailable)]
    [InlineData("{\"outcome\":\"SOMETHING_NEW\",\"matches\":true}", TransferVerifyOutcome.Unavailable)]
    [InlineData("{}", TransferVerifyOutcome.Unavailable)]
    [InlineData("not json", TransferVerifyOutcome.Unavailable)]
    // T157 düzeltme turu (S12) — the service's error bodies, should one ever
    // arrive with 200 (its error code turned into 200): not an answer, not a pass.
    [InlineData("{\"error\":\"RPC_UNAVAILABLE\",\"message\":\"The node could not answer.\"}", TransferVerifyOutcome.Unavailable)]
    [InlineData("{\"error\":\"RPC_RATE_LIMITED\",\"message\":\"x\",\"matches\":true}", TransferVerifyOutcome.Unavailable)]
    [InlineData("{\"error\":\"INVALID_VERIFY_REQUEST\",\"outcome\":\"MATCH\"}", TransferVerifyOutcome.Unavailable)]
    public async Task VerifyAnswer_OnlyAnExactMatchIsAMatch(string body, TransferVerifyOutcome expected)
    {
        var client = TransferClient(BlockchainNetwork.ETHEREUM, (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            }));

        var result = await client.VerifyTransferAsync(
            new TransferVerifyRequest("0x" + new string('a', 64), StablecoinType.USDT, null, "0xto", 1m),
            CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task VerifyError_IsNotVerified_NeverAMatch(HttpStatusCode status)
    {
        var client = TransferClient(BlockchainNetwork.ETHEREUM, (_, _) =>
            Task.FromResult(new HttpResponseMessage(status)));

        var result = await client.VerifyTransferAsync(
            new TransferVerifyRequest("0x" + new string('a', 64), StablecoinType.USDT, null, "0xto", 1m),
            CancellationToken.None);

        Assert.Equal(TransferVerifyOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task VerifyTransport_Failure_IsNotVerified()
    {
        var client = TransferClient(BlockchainNetwork.ETHEREUM, (_, _) =>
            throw new HttpRequestException("connection refused"));

        var result = await client.VerifyTransferAsync(
            new TransferVerifyRequest("0x" + new string('a', 64), StablecoinType.USDT, null, "0xto", 1m),
            CancellationToken.None);

        Assert.Equal(TransferVerifyOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task Verify_TronIsNotAsked()
    {
        var calls = 0;
        var client = TransferClient(BlockchainNetwork.TRON, (_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var result = await client.VerifyTransferAsync(
            new TransferVerifyRequest("tron-tx", StablecoinType.USDT, null, "TTo", 1m),
            CancellationToken.None);

        Assert.Equal(0, calls);
        Assert.Equal(TransferVerifyOutcome.NotSupported, result.Outcome);
    }

    private static HttpBlockchainTransferClient TransferClient(
        BlockchainNetwork network,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        const string baseUrl = "http://sidecar.test/";
        return new HttpBlockchainTransferClient(
            network,
            new HttpClient(new Handler(respond)) { BaseAddress = new Uri(baseUrl) },
            new BlockchainSidecarOptions { BaseUrl = baseUrl, InternalKey = "k" },
            NullLogger<HttpBlockchainTransferClient>.Instance);
    }

    private static HttpBlockchainSidecarClient Client(
        BlockchainNetwork network,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        const string baseUrl = "http://sidecar.test/";
        var http = new HttpClient(new Handler(respond)) { BaseAddress = new Uri(baseUrl) };
        return new HttpBlockchainSidecarClient(
            network,
            http,
            http,
            new BlockchainSidecarOptions { BaseUrl = baseUrl, InternalKey = "k" },
            NullLogger<HttpBlockchainSidecarClient>.Instance);
    }

    private static JsonNode Load(string folder, string file)
    {
        var path = Path.Combine(FindRepositoryRoot(), "sidecar-ethereum", "contracts", folder, file);
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

        throw new InvalidOperationException(
            $"Repository root not found walking up from '{AppContext.BaseDirectory}' (looked for docker-compose.yml).");
    }

    private sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
