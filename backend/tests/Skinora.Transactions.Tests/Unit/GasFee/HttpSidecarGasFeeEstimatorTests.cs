using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.Transactions.Tests.Unit.GasFee;

/// <summary>
/// Unit coverage for <see cref="HttpSidecarGasFeeEstimator"/>
/// (Prova-GasFeeChargedIsFixedGuess). Contract under test: a 200 with a
/// parsable <c>feeUsdt</c> yields that decimal; EVERY failure shape —
/// transport error, non-200, empty/garbled body, negative value — collapses
/// to <c>null</c> so the resolver can fall back instead of blocking a money
/// path.
/// </summary>
public class HttpSidecarGasFeeEstimatorTests
{
    private const string SidecarBaseUrl = "http://blockchain-sidecar.test/";

    private static readonly GasFeeEstimateRequest SampleRequest = new(
        FromAddress: "TDeposit000000000000000000000000000",
        ToAddress: "TBuyer0000000000000000000000000000000",
        Amount: 10.20m,
        Token: StablecoinType.USDT);

    [Fact]
    public async Task Ok_ParsesFeeUsdt_AndPostsExpectedBody()
    {
        HttpRequestMessage? observed = null;
        string? observedBody = null;
        var handler = new RecordingHandler(async (req, ct) =>
        {
            observed = req;
            observedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    feeUsdt = "0.18",
                    energyRequired = 29650,
                    energyAvailable = 100000,
                    energyShortfall = 0,
                    burnSun = 350000,
                    trxPriceUsdt = 0.5,
                    priceSource = "binance",
                }),
            };
        });
        var sut = BuildEstimator(handler, internalKey: "test-internal-key");

        var fee = await sut.EstimateFeeUsdtAsync(SampleRequest, CancellationToken.None);

        Assert.Equal(0.18m, fee);
        Assert.NotNull(observed);
        Assert.Equal("api/transfer/estimate-fee", observed!.RequestUri!.PathAndQuery.TrimStart('/'));
        Assert.Equal("test-internal-key", Assert.Single(observed.Headers.GetValues("X-Internal-Key")));
        using var body = JsonDocument.Parse(observedBody!);
        Assert.Equal("TDeposit000000000000000000000000000", body.RootElement.GetProperty("fromAddress").GetString());
        Assert.Equal("TBuyer0000000000000000000000000000000", body.RootElement.GetProperty("toAddress").GetString());
        Assert.Equal("10.2", body.RootElement.GetProperty("amount").GetString());
        Assert.Equal("USDT", body.RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public async Task NullSender_IsOmittedFromTheBody_NotSentAsNull()
    {
        // The payout shape. The sidecar reads an ABSENT fromAddress as "the hot
        // wallet" and rejects an explicit null with 400, so null must never
        // reach the wire (PayoutGasEstimateAlwaysFallsBack).
        string? observedBody = null;
        var handler = new RecordingHandler(async (req, ct) =>
        {
            observedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { feeUsdt = "0.10" }),
            };
        });

        var fee = await BuildEstimator(handler).EstimateFeeUsdtAsync(
            SampleRequest with { FromAddress = null }, CancellationToken.None);

        Assert.Equal(0.10m, fee);
        using var body = JsonDocument.Parse(observedBody!);
        Assert.False(
            body.RootElement.TryGetProperty("fromAddress", out _),
            $"fromAddress must be omitted, got: {observedBody}");
        Assert.Equal("TBuyer0000000000000000000000000000000", body.RootElement.GetProperty("toAddress").GetString());
    }

    [Fact]
    public async Task ZeroFee_IsReturnedAsZero()
    {
        var sut = BuildEstimator(RespondWith(new { feeUsdt = "0.00" }));

        var fee = await sut.EstimateFeeUsdtAsync(SampleRequest, CancellationToken.None);

        Assert.Equal(0m, fee);
    }

    [Fact]
    public async Task Non200_ReturnsNull()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = JsonContent.Create(new { error = "TRX_PRICE_UNAVAILABLE" }),
            }));

        var fee = await BuildEstimator(handler)
            .EstimateFeeUsdtAsync(SampleRequest, CancellationToken.None);

        Assert.Null(fee);
    }

    [Fact]
    public async Task Non200_LogsTheSidecarErrorCode()
    {
        // The status alone cannot say which rejection it was: a malformed
        // request (INVALID_ESTIMATE_REQUEST — recurs on every call, a contract
        // bug) and a missing token contract (TOKEN_CONTRACT_NOT_CONFIGURED)
        // both answer 400. The code in the log can.
        var logger = new ListLogger<HttpSidecarGasFeeEstimator>();
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new
                {
                    error = "INVALID_ESTIMATE_REQUEST",
                    message = "fromAddress must be a non-empty string",
                }),
            }));

        var fee = await BuildEstimator(handler, logger: logger)
            .EstimateFeeUsdtAsync(SampleRequest, CancellationToken.None);

        Assert.Null(fee);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("INVALID_ESTIMATE_REQUEST", entry.Message);
        Assert.Contains("fromAddress must be a non-empty string", entry.Message);
    }

    [Fact]
    public async Task Non200_WithUnreadableBody_StillReturnsNull()
    {
        // A proxy in front of the sidecar answers with HTML, not the error
        // envelope; reading the code is best-effort and must not turn a
        // fallback into an exception on a money path.
        var logger = new ListLogger<HttpSidecarGasFeeEstimator>();
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("<html>502 Bad Gateway</html>", Encoding.UTF8, "text/html"),
            }));

        var fee = await BuildEstimator(handler, logger: logger)
            .EstimateFeeUsdtAsync(SampleRequest, CancellationToken.None);

        Assert.Null(fee);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains("502", entry.Message);
    }

    [Theory]
    [InlineData("windows-1252")] // no code-pages provider registered: InvalidOperationException
    [InlineData("bogus")]        // unknown charset name: InvalidOperationException
    [InlineData("utf-7")]        // refused by .NET: NotSupportedException
    public async Task Non200_WithUndecodableCharset_StillReturnsNull(string charset)
    {
        // Reading the envelope decodes the body with the charset the response
        // declares, and a charset .NET cannot decode throws something other
        // than JsonException — none of which the outer filter catches. Before
        // ReadErrorAsync swallowed them, such an answer escaped the estimator
        // and aborted the payout batch instead of falling back (#327
        // validation; the main code fell back here because it read no body).
        var logger = new ListLogger<HttpSidecarGasFeeEstimator>();
        var handler = new RecordingHandler((_, _) =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(
                "{\"error\":\"INVALID_ESTIMATE_REQUEST\",\"message\":\"m\"}"));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = charset };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = content });
        });

        var fee = await BuildEstimator(handler, logger: logger)
            .EstimateFeeUsdtAsync(SampleRequest, CancellationToken.None);

        Assert.Null(fee);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("400", entry.Message);
    }

    [Fact]
    public async Task TransportFailure_ReturnsNull()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused")));

        var fee = await BuildEstimator(handler)
            .EstimateFeeUsdtAsync(SampleRequest, CancellationToken.None);

        Assert.Null(fee);
    }

    [Fact]
    public async Task UnparsableFee_ReturnsNull()
    {
        var fee = await BuildEstimator(RespondWith(new { feeUsdt = "not-a-number" }))
            .EstimateFeeUsdtAsync(SampleRequest, CancellationToken.None);

        Assert.Null(fee);
    }

    [Fact]
    public async Task NegativeFee_ReturnsNull()
    {
        var fee = await BuildEstimator(RespondWith(new { feeUsdt = "-1.00" }))
            .EstimateFeeUsdtAsync(SampleRequest, CancellationToken.None);

        Assert.Null(fee);
    }

    [Fact]
    public async Task GarbledJsonBody_ReturnsNull()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>oops</html>", Encoding.UTF8, "application/json"),
            }));

        var fee = await BuildEstimator(handler)
            .EstimateFeeUsdtAsync(SampleRequest, CancellationToken.None);

        Assert.Null(fee);
    }

    private static RecordingHandler RespondWith(object body) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(body),
        }));

    private static HttpSidecarGasFeeEstimator BuildEstimator(
        HttpMessageHandler handler,
        string internalKey = "",
        ILogger<HttpSidecarGasFeeEstimator>? logger = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(SidecarBaseUrl) };
        var options = Options.Create(new BlockchainSidecarOptions
        {
            BaseUrl = SidecarBaseUrl,
            InternalKey = internalKey,
        });
        return new HttpSidecarGasFeeEstimator(
            http, options, logger ?? NullLogger<HttpSidecarGasFeeEstimator>.Instance);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

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
