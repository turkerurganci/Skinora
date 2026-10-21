using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Tests.Helpers;

namespace Skinora.Transactions.Tests.Contract;

/// <summary>
/// T154 — the backend half of the Ethereum service's fee contract
/// (05 §3.3a, 08 §3.7.7), replayed against the example bodies the service's
/// own handler test uses (<c>sidecar-ethereum/contracts/</c>):
/// <list type="bullet">
///   <item>the estimate request bodies the backend sends for an Ethereum payout
///     and refund are byte-for-byte the examples the service accepts;</item>
///   <item>the example answer — <c>feeWei</c> 9,139,361,000,067,201, odd and
///     above 2^53 so no double holds it, and consistent with its own gas and
///     per-gas prices (T154 validation) — is charged only because <c>feeUsdt</c> follows exactly from it
///     and <c>ethPriceUsdt</c>; one cent off either way and the fallback is
///     charged instead;</item>
///   <item>the transfer-status example's <c>realizedFeeWei</c> (also beyond
///     2^53) arrives as the same integer.</item>
/// </list>
/// </summary>
public sealed class EthereumFeeWeiContractTests
{
    private const string BaseUrl = "http://ethereum-sidecar.test/";

    [Theory]
    [InlineData("payout.request.json")]
    [InlineData("refund.request.json")]
    public async Task Ethereum_Estimate_Request_Is_The_Body_The_Service_Accepts(string file)
    {
        var expected = LoadExample("estimate-fee", file);
        var sent = new List<string>();
        var estimator = BuildEstimator(async (request, ct) =>
        {
            sent.Add(await request.Content!.ReadAsStringAsync(ct));
            return Json(LoadExample("estimate-fee", "response.json").ToJsonString());
        });
        var resolver = ChargedGasFeeResolverFactory.Create(
            new FakeBlockchainServiceRegistry().With(BlockchainNetwork.ETHEREUM, gasFee: estimator),
            new DefaultSettings());

        var amount = decimal.Parse(Text(expected, "amount"), NumberStyles.Number, CultureInfo.InvariantCulture);
        if (expected["fromAddress"] is null)
        {
            await resolver.ResolvePayoutFeeAsync(
                BlockchainNetwork.ETHEREUM, Text(expected, "toAddress"), amount,
                StablecoinType.USDT, CancellationToken.None);
        }
        else
        {
            await resolver.ResolveRefundFeeAsync(
                BlockchainNetwork.ETHEREUM, Text(expected, "fromAddress"), Text(expected, "toAddress"),
                amount, StablecoinType.USDT, CancellationToken.None);
        }

        var actual = JsonNode.Parse(Assert.Single(sent));
        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"expected {expected.ToJsonString()}{Environment.NewLine}sent     {actual?.ToJsonString()}");
    }

    [Fact]
    public async Task Example_Answer_Is_Charged_And_Its_Wei_Reaches_The_Backend_Exactly()
    {
        var logger = new ListLogger<HttpSidecarGasFeeEstimator>();
        var estimator = BuildEstimator(
            (_, _) => Task.FromResult(Json(LoadExample("estimate-fee", "response.json").ToJsonString())),
            logger);

        var fee = await estimator.EstimateFeeUsdtAsync(Request, CancellationToken.None);

        Assert.Equal(22.07m, fee);
        // The wei figure is logged as the integer the service sent, not 9.1393610000672E+15.
        Assert.Contains(logger.Messages, m => m.Contains("9139361000067201 wei", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("22.06")] // a cent low
    [InlineData("22.08")] // a cent high
    [InlineData("0.02")]  // gwei mistaken for wei or similar unit slip
    public async Task An_Answer_Whose_FeeUsdt_Does_Not_Follow_From_Its_Wei_Is_Not_Charged(string feeUsdt)
    {
        var body = LoadExample("estimate-fee", "response.json");
        body["feeUsdt"] = feeUsdt;
        var estimator = BuildEstimator((_, _) => Task.FromResult(Json(body.ToJsonString())));

        Assert.Null(await estimator.EstimateFeeUsdtAsync(Request, CancellationToken.None));
    }

    [Theory]
    [InlineData("feeWei", null)]
    [InlineData("ethPriceUsdt", null)]
    [InlineData("feeWei", "9.139361000067201e15")]
    [InlineData("ethPriceUsdt", "2414.5799910")] // 7 decimals — not the contract
    public async Task An_Ethereum_Answer_Without_A_Usable_Wei_Or_Price_Is_Not_Charged(string field, string? value)
    {
        var body = LoadExample("estimate-fee", "response.json");
        body[field] = value is null ? null : JsonValue.Create(value);
        if (value is null) body.AsObject().Remove(field);
        var estimator = BuildEstimator((_, _) => Task.FromResult(Json(body.ToJsonString())));

        Assert.Null(await estimator.EstimateFeeUsdtAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task A_FeeWei_Sent_As_A_Json_Number_Is_Not_Charged()
    {
        // A number may already have lost digits on the way; the contract is a string.
        var json = LoadExample("estimate-fee", "response.json").ToJsonString()
            .Replace("\"feeWei\":\"9139361000067201\"", "\"feeWei\":9139361000067201", StringComparison.Ordinal);
        Assert.Contains("\"feeWei\":9139361000067201", json, StringComparison.Ordinal);
        var estimator = BuildEstimator((_, _) => Task.FromResult(Json(json)));

        Assert.Null(await estimator.EstimateFeeUsdtAsync(Request, CancellationToken.None));
    }

    // -------------------- T160b — T159b's "block not found" --------------------

    [Fact]
    public async Task T160b_The_T159b_Block_Not_Found_Answer_Is_Retryable()
    {
        // The exact body the service answered twice in T159b (B1), as its
        // handler test produces it: 502 + retryable: true.
        var estimator = BuildEstimator((_, _) => Task.FromResult(BlockNotFound()));

        var attempt = await estimator.EstimateFeeAsync(Request, CancellationToken.None);

        Assert.Null(attempt.FeeUsdt);
        Assert.True(attempt.Retryable);
        Assert.Contains("FEE_ESTIMATE_SIMULATION_FAILED", attempt.Failure);
    }

    [Fact]
    public async Task T160b_After_Block_Not_Found_The_Refund_Is_Asked_Again_And_The_Measured_Fee_Charged_Not_The_Fallback()
    {
        // Before T160b the single attempt charged the 0.50 fallback here. The
        // second answer is a mainnet-like deposit refund (0.09) — apart from
        // the 0.50 setting so a fallback cannot pass for it.
        var second = LoadExample("estimate-fee", "response.json");
        second["feeWei"] = "34400000000000";
        second["ethPriceUsdt"] = "2364.48";
        second["feeUsdt"] = "0.09";
        var calls = 0;
        var estimator = BuildEstimator((_, _) => Task.FromResult(
            Interlocked.Increment(ref calls) == 1 ? BlockNotFound() : Json(second.ToJsonString())));
        var resolver = ChargedGasFeeResolverFactory.Create(
            new FakeBlockchainServiceRegistry().With(BlockchainNetwork.ETHEREUM, gasFee: estimator),
            new DefaultSettings(),
            new GasFeeEstimateRetryPolicy(MaxAttempts: 3, Delays: [TimeSpan.Zero], Budget: TimeSpan.FromSeconds(12)));

        var resolved = await resolver.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, Request.FromAddress, Request.ToAddress, Request.Amount,
            StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal(0.09m, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.RuntimeEstimate, resolved.Source);
        Assert.NotEqual(
            BlockchainNetworkSettingsProvider.EthereumDefaults.RefundGasFeeFallbackUsdt, resolved.FeeUsdt);
    }

    private static HttpResponseMessage BlockNotFound() =>
        new(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(
                LoadExample("estimate-fee", "error.block-not-found.json").ToJsonString(),
                Encoding.UTF8,
                "application/json"),
        };

    [Fact]
    public async Task Tron_Answers_Are_Not_Asked_For_Wei()
    {
        // TRON's answer has no feeWei; its feeUsdt is charged as before.
        var estimator = new HttpSidecarGasFeeEstimator(
            BlockchainNetwork.TRON,
            new HttpClient(new Handler((_, _) => Task.FromResult(Json("{\"feeUsdt\":\"2.90\"}"))))
            {
                BaseAddress = new Uri(BaseUrl),
            },
            new BlockchainSidecarOptions { BaseUrl = BaseUrl },
            NullLogger<HttpSidecarGasFeeEstimator>.Instance);

        Assert.Equal(2.90m, await estimator.EstimateFeeUsdtAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task Transfer_Status_Example_Delivers_The_Realized_Fee_In_Wei_Exactly()
    {
        var body = LoadExample("transfer-status", "confirmed.response.json");
        var client = new HttpBlockchainTransferClient(
            BlockchainNetwork.ETHEREUM,
            new HttpClient(new Handler((_, _) => Task.FromResult(Json(body.ToJsonString()))))
            {
                BaseAddress = new Uri(BaseUrl),
            },
            new BlockchainSidecarOptions { BaseUrl = BaseUrl },
            NullLogger<HttpBlockchainTransferClient>.Instance);

        var status = await client.GetStatusAsync(Text(body, "txHash"), CancellationToken.None);

        Assert.Equal(TransferStatusOutcome.Confirmed, status.Outcome);
        Assert.Equal(9_007_282_507_774_707m, status.RealizedFeeWei);
        Assert.Equal(Text(body, "realizedFeeWei"), status.RealizedFeeWei!.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static readonly GasFeeEstimateRequest Request = new(
        "0x9858effd232b4033e47d90003d41ec34ecaeda94",
        "0xfb6916095ca1df60bb79ce92ce3ea74c37c5d359",
        10.2m,
        StablecoinType.USDT);

    private static HttpSidecarGasFeeEstimator BuildEstimator(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        ILogger<HttpSidecarGasFeeEstimator>? logger = null) =>
        new(
            BlockchainNetwork.ETHEREUM,
            new HttpClient(new Handler(respond)) { BaseAddress = new Uri(BaseUrl) },
            new BlockchainSidecarOptions { BaseUrl = BaseUrl },
            logger ?? NullLogger<HttpSidecarGasFeeEstimator>.Instance);

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

    private sealed class DefaultSettings : IBlockchainNetworkSettingsProvider
    {
        public Task<BlockchainNetworkSettings> GetAsync(
            BlockchainNetwork network, CancellationToken cancellationToken) =>
            Task.FromResult(BlockchainNetworkSettingsProvider.DefaultsFor(network));
    }

    private sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
