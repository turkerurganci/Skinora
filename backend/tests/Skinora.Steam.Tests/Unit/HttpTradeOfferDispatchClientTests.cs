using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Steam.Application.Dispatch;
using Skinora.Steam.Application.Inventory;

namespace Skinora.Steam.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="HttpTradeOfferDispatchClient"/> (T106a) — a stub
/// <see cref="HttpMessageHandler"/> drives the request-shape assertions
/// (URI / method / header / camelCase + lowercase item keys) and the HTTP →
/// <see cref="TradeOfferDispatchStatus"/> mapping (200 / 502 / 400 / 503 /
/// transport).
/// </summary>
public sealed class HttpTradeOfferDispatchClientTests
{
    private const string InternalKey = "test-internal-key";

    private static readonly TradeOfferDispatchRequest SampleRequest = new(
        TransactionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Direction: TradeOfferDispatchDirection.SellerToBot,
        PartnerSteamId: "76561198000000101",
        Items: [new TradeOfferDispatchItem("asset-1", 730, "2")],
        BotAccountName: "EscrowBot-1");

    [Fact]
    public async Task SendAsync_Sent_On_200_Sent()
    {
        var handler = new StubHandler(_ => OkJson(@"{""status"":""sent"",""offerId"":""9001"",""attempts"":1}"));
        var sut = BuildClient(handler);

        var result = await sut.SendAsync(SampleRequest, CancellationToken.None);

        Assert.Equal(TradeOfferDispatchStatus.Sent, result.Status);
        Assert.Equal("9001", result.OfferId);
        Assert.False(result.Retryable);
    }

    [Fact]
    public async Task SendAsync_Pending_On_200_Pending()
    {
        var handler = new StubHandler(_ => OkJson(@"{""status"":""pending"",""offerId"":""9002"",""attempts"":1}"));
        var sut = BuildClient(handler);

        var result = await sut.SendAsync(SampleRequest, CancellationToken.None);

        Assert.Equal(TradeOfferDispatchStatus.Pending, result.Status);
        Assert.Equal("9002", result.OfferId);
    }

    [Fact]
    public async Task SendAsync_Failed_Retryable_On_502()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(
                @"{""status"":""failed"",""reason"":""NoConnection"",""retryable"":true,""attempts"":3}",
                Encoding.UTF8, "application/json"),
        });
        var sut = BuildClient(handler);

        var result = await sut.SendAsync(SampleRequest, CancellationToken.None);

        Assert.Equal(TradeOfferDispatchStatus.Failed, result.Status);
        Assert.True(result.Retryable);
        Assert.Equal(3, result.Attempts);
        Assert.Equal("NoConnection", result.Reason);
    }

    [Fact]
    public async Task SendAsync_Failed_NonRetryable_On_400()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(@"{""error"":""items must be a non-empty array""}", Encoding.UTF8, "application/json"),
        });
        var sut = BuildClient(handler);

        var result = await sut.SendAsync(SampleRequest, CancellationToken.None);

        Assert.Equal(TradeOfferDispatchStatus.Failed, result.Status);
        Assert.False(result.Retryable);
    }

    [Fact]
    public async Task SendAsync_Unavailable_On_503()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var sut = BuildClient(handler);

        var result = await sut.SendAsync(SampleRequest, CancellationToken.None);

        Assert.Equal(TradeOfferDispatchStatus.Unavailable, result.Status);
        Assert.True(result.Retryable);
    }

    [Fact]
    public async Task SendAsync_Unavailable_On_TransportError()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var sut = BuildClient(handler);

        var result = await sut.SendAsync(SampleRequest, CancellationToken.None);

        Assert.Equal(TradeOfferDispatchStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task SendAsync_Posts_To_Send_Path_With_Key_And_Sidecar_Item_Shape()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new StubHandler(req =>
        {
            captured = req;
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return OkJson(@"{""status"":""sent"",""offerId"":""9003"",""attempts"":1}");
        });
        var sut = BuildClient(handler);

        await sut.SendAsync(SampleRequest, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.EndsWith("/api/trade-offers/send", captured.RequestUri!.AbsoluteUri);
        Assert.True(captured.Headers.TryGetValues("X-Internal-Key", out var keyValues));
        Assert.Equal(InternalKey, keyValues!.Single());

        // The sidecar's ItemDescriptor uses lowercase assetid/appid/contextid.
        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("SELLER_TO_BOT", doc.RootElement.GetProperty("direction").GetString());
        Assert.Equal("76561198000000101", doc.RootElement.GetProperty("partnerSteamId").GetString());
        Assert.Equal("EscrowBot-1", doc.RootElement.GetProperty("botAccountName").GetString());
        var item = doc.RootElement.GetProperty("items")[0];
        Assert.Equal("asset-1", item.GetProperty("assetid").GetString());
        Assert.Equal(730, item.GetProperty("appid").GetInt32());
        Assert.Equal("2", item.GetProperty("contextid").GetString());
    }

    // ---------- helpers ----------

    private static HttpResponseMessage OkJson(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpTradeOfferDispatchClient BuildClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://sidecar.test/") };
        var options = Options.Create(new SteamSidecarOptions
        {
            BaseUrl = "http://sidecar.test",
            InternalKey = InternalKey,
            TimeoutSeconds = 30,
        });
        return new HttpTradeOfferDispatchClient(
            http, options, NullLogger<HttpTradeOfferDispatchClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
