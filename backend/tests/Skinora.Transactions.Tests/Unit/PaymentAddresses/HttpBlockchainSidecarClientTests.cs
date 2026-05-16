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
