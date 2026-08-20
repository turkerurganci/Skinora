using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.Transactions.Tests.Unit.PaymentAddresses;

/// <summary>
/// Unit coverage for <see cref="HttpBlockchainSidecarClient"/>. Drives the
/// HTTP outcome surface via a stub <see cref="HttpMessageHandler"/> so the
/// tests run without Docker or a live sidecar.
/// </summary>
public class HttpBlockchainSidecarClientTests
{
    private const string SidecarBaseUrl = "http://blockchain-sidecar.test/";

    [Fact]
    public async Task Returns_Success_On_200_With_Address_Payload()
    {
        var handler = new RecordingHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.EndsWith("/api/wallet/derive", req.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    address = "TV54FrPiVbUqxAuDMH6nKT32DPWzNwcpUu",
                    derivationPath = "m/44'/195'/0'/0/0",
                    index = 0,
                }),
            });
        });

        var sut = BuildClient(handler);

        var result = await sut.DeriveAddressAsync(0, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.Success, result.Status);
        Assert.Equal("TV54FrPiVbUqxAuDMH6nKT32DPWzNwcpUu", result.Address);
        Assert.Equal("m/44'/195'/0'/0/0", result.DerivationPath);
    }

    [Fact]
    public async Task Sends_Internal_Key_Header_When_Configured()
    {
        string? observedHeader = null;
        var handler = new RecordingHandler((req, _) =>
        {
            observedHeader = req.Headers.TryGetValues("X-Internal-Key", out var values)
                ? string.Join(",", values)
                : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    address = "TV54FrPiVbUqxAuDMH6nKT32DPWzNwcpUu",
                    derivationPath = "m/44'/195'/0'/0/0",
                    index = 0,
                }),
            });
        });

        var sut = BuildClient(handler, internalKey: "super-secret-key");

        await sut.DeriveAddressAsync(0, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("super-secret-key", observedHeader);
    }

    [Fact]
    public async Task Returns_NotConfigured_On_503()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var sut = BuildClient(handler);

        var result = await sut.DeriveAddressAsync(0, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.NotConfigured, result.Status);
        Assert.Null(result.Address);
    }

    [Fact]
    public async Task Returns_InvalidRequest_On_400()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        var sut = BuildClient(handler);

        var result = await sut.DeriveAddressAsync(-1, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.InvalidRequest, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Returns_Unavailable_On_5xx(HttpStatusCode status)
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(status)));

        var sut = BuildClient(handler);

        var result = await sut.DeriveAddressAsync(0, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Returns_Unavailable_On_Network_Exception()
    {
        var handler = new RecordingHandler((_, _) =>
            throw new HttpRequestException("connection refused"));

        var sut = BuildClient(handler);

        var result = await sut.DeriveAddressAsync(0, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Returns_Unavailable_On_Empty_Body()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            }));

        var sut = BuildClient(handler);

        var result = await sut.DeriveAddressAsync(0, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Returns_Unavailable_On_Malformed_Json()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not json", System.Text.Encoding.UTF8, "application/json"),
            }));

        var sut = BuildClient(handler);

        var result = await sut.DeriveAddressAsync(0, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Posts_Index_And_TransactionId_In_Body()
    {
        var capturedBody = new List<string>();
        var handler = new RecordingHandler(async (req, _) =>
        {
            capturedBody.Add(await req.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    address = "TV54FrPiVbUqxAuDMH6nKT32DPWzNwcpUu",
                    derivationPath = "m/44'/195'/0'/0/42",
                    index = 42,
                }),
            };
        });

        var sut = BuildClient(handler);
        var transactionId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        await sut.DeriveAddressAsync(42, transactionId, CancellationToken.None);

        Assert.Single(capturedBody);
        Assert.Contains("\"index\":42", capturedBody[0]);
        Assert.Contains(transactionId.ToString("D"), capturedBody[0]);
    }

    // ─── Monitor commands (T139; düzeltme turu 2 — bulgu N1) ────────────
    //
    // Everything the monitor lifecycle depends on lives in the four lines of
    // SendCommandAsync that these tests cover, and nothing covered it before:
    // the request PATH (a typo is a permanent 404 → Unavailable → an outbox
    // retry loop), the JSON FIELD NAMES (the sidecar rejects a payload missing
    // any of its five required fields with 400 INVALID_MONITOR_REQUEST), and
    // the STATUS MAPPING that decides whether a failure is terminal or
    // retried — PaymentMonitorStartDispatcher swallows InvalidRequest and
    // rethrows everything else, so getting that wrong is the difference
    // between a retried call and a silently dead payment window.

    [Fact]
    public async Task StartMonitoring_Posts_The_Five_Required_Fields_To_The_Monitor_Start_Path()
    {
        string? path = null;
        string? body = null;
        var handler = new RecordingHandler(async (req, _) =>
        {
            path = req.RequestUri!.AbsolutePath;
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var paymentAddressId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
        var transactionId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

        var status = await BuildClient(handler).StartMonitoringAsync(
            new PaymentMonitorStartRequest(
                Address: "TDepositAddrFakeFakeFakeFakeFake123",
                PaymentAddressId: paymentAddressId,
                TransactionId: transactionId,
                ExpectedContract: "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
                ExpectedSymbol: "USDT"),
            CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.Success, status);
        Assert.Equal("/api/monitor/start", path);

        // The names are the contract: sidecar startMonitorHandler rejects the
        // payload with 400 INVALID_MONITOR_REQUEST if any of the five is absent.
        Assert.Contains("\"address\":\"TDepositAddrFakeFakeFakeFakeFake123\"", body);
        Assert.Contains($"\"paymentAddressId\":\"{paymentAddressId:D}\"", body);
        Assert.Contains($"\"transactionId\":\"{transactionId:D}\"", body);
        Assert.Contains("\"expectedContract\":\"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t\"", body);
        // The sidecar validates this against its own USDT/USDC allowlist, so it
        // must be the enum NAME, not its numeric value.
        Assert.Contains("\"expectedSymbol\":\"USDT\"", body);
    }

    [Fact]
    public async Task StopMonitoring_Posts_The_Address_To_The_Monitor_Stop_Path()
    {
        string? path = null;
        string? body = null;
        var handler = new RecordingHandler(async (req, _) =>
        {
            path = req.RequestUri!.AbsolutePath;
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var status = await BuildClient(handler).StopMonitoringAsync(
            "TDepositAddrFakeFakeFakeFakeFake123", CancellationToken.None);

        Assert.Equal(BlockchainSidecarStatus.Success, status);
        Assert.Equal("/api/monitor/stop", path);
        Assert.Equal("{\"address\":\"TDepositAddrFakeFakeFakeFakeFake123\"}", body);
    }

    [Fact]
    public async Task PostCancel_Commands_Use_Their_Own_Paths()
    {
        // The post-cancel twins share SendCommandAsync, so they share every
        // mapping asserted below; only their paths differ and nothing pinned
        // them either.
        var paths = new List<string>();
        var handler = new RecordingHandler((req, _) =>
        {
            paths.Add(req.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var sut = BuildClient(handler);

        await sut.StartPostCancelMonitoringAsync(
            new PostCancelMonitorStartRequest(
                Address: "TDepositAddrFakeFakeFakeFakeFake123",
                PaymentAddressId: Guid.NewGuid(),
                TransactionId: Guid.NewGuid(),
                ExpectedContract: "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
                ExpectedSymbol: "USDT",
                CancelledAt: new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);
        await sut.StopPostCancelMonitoringAsync(
            "TDepositAddrFakeFakeFakeFakeFake123", CancellationToken.None);

        Assert.Equal(
            new[] { "/api/monitor/post-cancel-start", "/api/monitor/post-cancel-stop" },
            paths);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, BlockchainSidecarStatus.Success)]
    [InlineData(HttpStatusCode.NoContent, BlockchainSidecarStatus.Success)]
    // Terminal for the dispatcher: the payload itself is wrong, so redelivering
    // it would fail identically.
    [InlineData(HttpStatusCode.BadRequest, BlockchainSidecarStatus.InvalidRequest)]
    [InlineData(HttpStatusCode.ServiceUnavailable, BlockchainSidecarStatus.NotConfigured)]
    // Retried: the dispatcher rethrows so the outbox redelivers.
    [InlineData(HttpStatusCode.InternalServerError, BlockchainSidecarStatus.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, BlockchainSidecarStatus.Unavailable)]
    [InlineData(HttpStatusCode.NotFound, BlockchainSidecarStatus.Unavailable)]
    [InlineData(HttpStatusCode.Unauthorized, BlockchainSidecarStatus.Unavailable)]
    public async Task Monitor_Command_Maps_Http_Status_To_Sidecar_Status(
        HttpStatusCode responseCode, BlockchainSidecarStatus expected)
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(responseCode)));

        Assert.Equal(
            expected,
            await BuildClient(handler).StartMonitoringAsync(
                BuildMonitorStartRequest(), CancellationToken.None));
        Assert.Equal(
            expected,
            await BuildClient(handler).StopMonitoringAsync("TAddr", CancellationToken.None));
    }

    [Fact]
    public async Task Monitor_Command_Maps_Transport_Failure_To_Unavailable()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("sidecar down")));

        Assert.Equal(
            BlockchainSidecarStatus.Unavailable,
            await BuildClient(handler).StartMonitoringAsync(
                BuildMonitorStartRequest(), CancellationToken.None));
        Assert.Equal(
            BlockchainSidecarStatus.Unavailable,
            await BuildClient(handler).StopMonitoringAsync("TAddr", CancellationToken.None));
    }

    [Fact]
    public async Task Monitor_Command_Maps_Timeout_To_Unavailable()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout")));

        Assert.Equal(
            BlockchainSidecarStatus.Unavailable,
            await BuildClient(handler).StartMonitoringAsync(
                BuildMonitorStartRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task Monitor_Command_Sends_The_Internal_Key_Header()
    {
        string? observed = null;
        var handler = new RecordingHandler((req, _) =>
        {
            observed = req.Headers.TryGetValues("X-Internal-Key", out var values)
                ? string.Join(",", values)
                : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        await BuildClient(handler, internalKey: "monitor-secret").StartMonitoringAsync(
            BuildMonitorStartRequest(), CancellationToken.None);

        Assert.Equal("monitor-secret", observed);
    }

    private static PaymentMonitorStartRequest BuildMonitorStartRequest()
        => new(
            Address: "TDepositAddrFakeFakeFakeFakeFake123",
            PaymentAddressId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            ExpectedContract: "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
            ExpectedSymbol: "USDT");

    private static HttpBlockchainSidecarClient BuildClient(
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
        return new HttpBlockchainSidecarClient(http, options, NullLogger<HttpBlockchainSidecarClient>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }
}
