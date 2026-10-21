using Skinora.Transactions.Tests.Helpers;
using Skinora.Transactions.Application.Networks;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.Transactions.Tests.Contract;

/// <summary>
/// Pins the backend half of the blockchain sidecar's
/// <c>POST /api/transfer/estimate-fee</c> request contract against the example
/// bodies the sidecar's own handler test replays
/// (<c>sidecar-blockchain/contracts/estimate-fee/</c>).
/// </summary>
/// <remarks>
/// <para>
/// Why this exists (PayoutGasEstimateAlwaysFallsBack, 2026-09-23 live
/// rehearsal): the payout path serialized its hot-wallet sender as
/// <c>"fromAddress": null</c>. The sidecar accepts the key only when it is
/// absent or a non-empty string, so every payout estimate came back 400 and
/// the seller was silently charged the static fallback. Both halves were
/// unit-tested — the backend against its own idea of the body, the sidecar
/// against hand-written requests — and neither ever saw the other's bytes.
/// Refunds hid it: they always carry a deposit address, so theirs went through.
/// </para>
/// <para>
/// The bodies are produced by the real <see cref="ChargedGasFeeResolver"/> and
/// <see cref="HttpSidecarGasFeeEstimator"/>, so a regression in either — the
/// resolver's choice of sender, the serializer's null handling, the amount
/// format — breaks this test. The sidecar test breaks if the handler stops
/// accepting the same files, or if an example's amount stops passing the
/// service's own amount rule.
/// </para>
/// <para>
/// The payout example carries four integer and six fraction digits on
/// purpose: USDT and USDC have six decimals, and only a value that uses them
/// all pins the format. The first examples ("10", "10.2") read the same under
/// "0.#" as under "0.######", so a format that dropped precision — or added a
/// group separator the sidecar rejects — left both sides green (#327
/// validation).
/// </para>
/// </remarks>
public sealed class BlockchainSidecarEstimateFeeContractTests
{
    [Fact]
    public async Task PayoutRequest_IsTheBodyTheSidecarAccepts()
    {
        var expected = LoadExample("payout.request.json");
        var (resolver, sent) = BuildResolver();

        await resolver.ResolvePayoutFeeAsync(
            Network(expected),
            Text(expected, "toAddress"),
            Amount(expected),
            Token(expected),
            CancellationToken.None);

        AssertSameJson(expected, Assert.Single(sent));
    }

    [Fact]
    public async Task RefundRequest_IsTheBodyTheSidecarAccepts()
    {
        var expected = LoadExample("refund.request.json");
        var (resolver, sent) = BuildResolver();

        await resolver.ResolveRefundFeeAsync(
            Network(expected),
            Text(expected, "fromAddress"),
            Text(expected, "toAddress"),
            Amount(expected),
            Token(expected),
            CancellationToken.None);

        AssertSameJson(expected, Assert.Single(sent));
    }

    /// <summary>
    /// T149 — <c>POST /api/transfer/estimate-sweep-fee</c>: the body the real
    /// quote service + estimator send is the sidecar's example
    /// (<c>sweep.request.json</c>), and the answer in the shape the sidecar's
    /// handler returns (its <c>SweepCostEstimateResult</c>) parses into the
    /// buyer's line: 8,441,349 SUN × 0.34 × (1 + 0.007) = 2.8901 → 2.90.
    /// </summary>
    [Fact]
    public async Task SweepRequest_IsTheBodyTheSidecarAccepts_AndItsAnswerBecomesTheLine()
    {
        var expected = LoadExample("sweep.request.json");
        var sent = new List<(string Path, string Body)>();
        var handler = new RecordingHandler(async (request, ct) =>
        {
            sent.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                // The sidecar's SweepCostEstimateResult, field for field.
                Content = JsonContent.Create(new
                {
                    sweepCostSun = 8_441_349,
                    activationSun = 1_100_000,
                    burnTopUpSun = 7_071_349,
                    bandwidthSun = 270_000,
                    energyRequired = 64_285,
                    trxPriceUsdt = 0.34,
                    priceSource = "binance",
                    costUsdt = "2.870059",
                }),
            };
        });
        const string baseUrl = "http://blockchain-sidecar.test/";
        var estimator = new HttpSidecarGasFeeEstimator(
            BlockchainNetwork.TRON,
            new HttpClient(handler) { BaseAddress = new Uri(baseUrl) },
            new BlockchainSidecarOptions { BaseUrl = baseUrl },
            NullLogger<HttpSidecarGasFeeEstimator>.Instance);
        var quotes = new NetworkFeeQuoteService(
            FakeBlockchainServiceRegistry.ForTron(gasFee: estimator),
            new StubSettings(),
            new SweepCostEstimateCache(TimeProvider.System),
            NullLogger<NetworkFeeQuoteService>.Instance);

        var quote = await quotes.GetBuyerNetworkFeeAsync(
            Network(expected), Token(expected), CancellationToken.None);

        var (path, body) = Assert.Single(sent);
        Assert.Equal("/api/transfer/estimate-sweep-fee", path);
        AssertSameJson(expected, body);
        Assert.Equal(NetworkFeeSource.RuntimeEstimate, quote.Source);
        Assert.Equal(2.90m, quote.Amount);
    }

    private static void AssertSameJson(JsonNode expected, string actualBody)
    {
        var actual = JsonNode.Parse(actualBody);
        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            "The backend no longer sends the body the sidecar's handler test accepts."
            + Environment.NewLine + "  expected: " + expected.ToJsonString()
            + Environment.NewLine + "  sent:     " + actual?.ToJsonString());
    }

    private static (ChargedGasFeeResolver Resolver, List<string> Sent) BuildResolver()
    {
        var sent = new List<string>();
        var handler = new RecordingHandler(async (request, ct) =>
        {
            sent.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { feeUsdt = "0.10" }),
            };
        });

        const string baseUrl = "http://blockchain-sidecar.test/";
        var estimator = new HttpSidecarGasFeeEstimator(
            BlockchainNetwork.TRON,
            new HttpClient(handler) { BaseAddress = new Uri(baseUrl) },
            new BlockchainSidecarOptions { BaseUrl = baseUrl },
            NullLogger<HttpSidecarGasFeeEstimator>.Instance);
        var resolver = ChargedGasFeeResolverFactory.Create(
            FakeBlockchainServiceRegistry.ForTron(gasFee: estimator),
            new StubSettings());
        return (resolver, sent);
    }

    private static JsonNode LoadExample(string fileName)
    {
        var path = Path.Combine(
            FindRepositoryRoot(), "sidecar-blockchain", "contracts", "estimate-fee", fileName);
        return JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{path} is empty.");
    }

    private static string Text(JsonNode body, string key) =>
        body[key]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Example body has no '{key}'.");

    private static decimal Amount(JsonNode body) =>
        decimal.Parse(Text(body, "amount"), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static StablecoinType Token(JsonNode body) =>
        Enum.Parse<StablecoinType>(Text(body, "token"));

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Repository root not found walking up from '{AppContext.BaseDirectory}' "
            + "(looked for docker-compose.yml).");
    }

    private sealed class StubSettings : IBlockchainNetworkSettingsProvider
    {
        public Task<BlockchainNetworkSettings> GetAsync(
            BlockchainNetwork network, CancellationToken cancellationToken) =>
            Task.FromResult(BlockchainNetworkSettingsProvider.DefaultsFor(network));
    }

    // T148 — the example body names its network; the request is priced on it.
    private static BlockchainNetwork Network(JsonNode body) =>
        Enum.Parse<BlockchainNetwork>(Text(body, "network"));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            _respond(request, cancellationToken);
    }
}
