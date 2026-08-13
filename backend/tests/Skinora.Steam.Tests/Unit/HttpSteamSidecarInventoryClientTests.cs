using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Steam.Application.Inventory;
using Skinora.Transactions.Application.Steam;

namespace Skinora.Steam.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="HttpSteamSidecarInventoryClient"/>. Use a stub
/// <see cref="HttpMessageHandler"/> so the assertions cover the request
/// shape (URI, method, headers), JSON parsing, and the discriminated
/// outcome mapping (200 / 422 / 5xx / transport failure).
/// </summary>
public sealed class HttpSteamSidecarInventoryClientTests
{
    private const string SteamId = "76561198000000001";
    private const string InternalKey = "test-internal-key";

    [Fact]
    public async Task GetInventoryAsync_Returns_Success_With_Payload_On_200()
    {
        const string json = @"{
            ""items"": [
                {
                    ""assetId"": ""27348562891"",
                    ""classId"": ""310776959"",
                    ""instanceId"": ""188530139"",
                    ""name"": ""AK-47 | Redline"",
                    ""marketHashName"": ""AK-47 | Redline (Field-Tested)"",
                    ""type"": ""Rifle"",
                    ""exterior"": ""Field-Tested"",
                    ""iconUrl"": ""https://cdn.test/ak.png"",
                    ""tradable"": true,
                    ""marketable"": true
                }
            ],
            ""totalCount"": 1,
            ""tradeableCount"": 1
        }";
        var handler = new StubHandler(_ => OkJson(json));
        var sut = BuildClient(handler);

        var result = await sut.GetInventoryAsync(SteamId, bypassCache: false, CancellationToken.None);

        Assert.Equal(SteamSidecarStatus.Success, result.Status);
        Assert.NotNull(result.Inventory);
        Assert.Equal(1, result.Inventory!.TotalCount);
        Assert.Equal(1, result.Inventory.TradeableCount);
        Assert.Equal("AK-47 | Redline", result.Inventory.Items[0].Name);
        // 08 §2.3 wear → 07 §6.1 "wear" maps from sidecar exterior.
        Assert.Equal("Field-Tested", result.Inventory.Items[0].Wear);
        Assert.True(result.Inventory.Items[0].Tradeable);
    }

    [Fact]
    public async Task GetInventoryAsync_Accepts_The_T120_Visibility_Field_On_200()
    {
        // T120 added `visibility` to the 200 body alongside the status code.
        // Pins the sidecar↔backend field tolerance the T120 validator flagged
        // as untested (observation V3): a PUBLIC body parses normally and the
        // extra field changes nothing about the payload.
        const string json = @"{
            ""visibility"": ""PUBLIC"",
            ""items"": [
                {
                    ""assetId"": ""27348562891"",
                    ""classId"": ""310776959"",
                    ""instanceId"": ""188530139"",
                    ""name"": ""AK-47 | Redline"",
                    ""marketHashName"": ""AK-47 | Redline (Field-Tested)"",
                    ""type"": ""Rifle"",
                    ""exterior"": ""Field-Tested"",
                    ""iconUrl"": ""https://cdn.test/ak.png"",
                    ""tradable"": true,
                    ""marketable"": true
                }
            ],
            ""totalCount"": 1,
            ""tradeableCount"": 1
        }";
        var handler = new StubHandler(_ => OkJson(json));
        var sut = BuildClient(handler);

        var result = await sut.GetInventoryAsync(SteamId, bypassCache: false, CancellationToken.None);

        Assert.Equal(SteamSidecarStatus.Success, result.Status);
        Assert.Equal(1, result.Inventory!.TotalCount);
        Assert.Equal("27348562891", result.Inventory.Items[0].AssetId);
    }

    [Theory]
    [InlineData("PRIVATE", SteamSidecarStatus.InventoryPrivate)]
    [InlineData("private", SteamSidecarStatus.InventoryPrivate)]
    [InlineData("UNAVAILABLE", SteamSidecarStatus.Unavailable)]
    // An unknown value is absence of information, never "readable".
    [InlineData("SOMETHING_NEW", SteamSidecarStatus.Unavailable)]
    public async Task GetInventoryAsync_Honours_A_NonPublic_Visibility_On_200(
        string visibility, SteamSidecarStatus expected)
    {
        // T121 — the silent failure this guards against: a 200 whose body says
        // the inventory was not readable would otherwise be shipped upstream as
        // an EMPTY inventory, and the seller would be told "item not in your
        // inventory" (T120 §"Reddedilen tasarım").
        var json = $@"{{""visibility"":""{visibility}"",""items"":[],""totalCount"":0,""tradeableCount"":0}}";
        var handler = new StubHandler(_ => OkJson(json));
        var sut = BuildClient(handler);

        var result = await sut.GetInventoryAsync(SteamId, bypassCache: false, CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Inventory);
    }

    [Fact]
    public async Task GetInventoryAsync_Treats_A_Missing_Visibility_As_Public()
    {
        // Backwards compatibility with a sidecar predating T120: the HTTP
        // status is the normative contract (07 §6.1), so a 200 without the
        // field stays a successful read.
        var handler = new StubHandler(_ =>
            OkJson("{\"items\":[],\"totalCount\":0,\"tradeableCount\":0}"));
        var sut = BuildClient(handler);

        var result = await sut.GetInventoryAsync(SteamId, bypassCache: false, CancellationToken.None);

        Assert.Equal(SteamSidecarStatus.Success, result.Status);
        Assert.NotNull(result.Inventory);
        Assert.Empty(result.Inventory!.Items);
    }

    [Fact]
    public async Task GetInventoryAsync_Returns_Unavailable_When_200_Body_Has_No_Items_Array()
    {
        // A 200 without `items` is a broken envelope, not an empty inventory.
        // Reading it as empty would manufacture evidence that an asset is gone.
        var handler = new StubHandler(_ => OkJson("{\"totalCount\":0,\"tradeableCount\":0}"));
        var sut = BuildClient(handler);

        var result = await sut.GetInventoryAsync(SteamId, bypassCache: false, CancellationToken.None);

        Assert.Equal(SteamSidecarStatus.Unavailable, result.Status);
        Assert.Null(result.Inventory);
    }

    [Fact]
    public async Task GetInventoryAsync_Includes_InternalKey_Header()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req =>
        {
            captured = req;
            return OkJson("{\"items\":[],\"totalCount\":0,\"tradeableCount\":0}");
        });
        var sut = BuildClient(handler);

        await sut.GetInventoryAsync(SteamId, bypassCache: false, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.EndsWith($"/api/inventory/{SteamId}", captured.RequestUri!.AbsoluteUri);
        Assert.True(captured.Headers.TryGetValues("X-Internal-Key", out var keyValues));
        Assert.Equal(InternalKey, keyValues!.Single());
    }

    [Fact]
    public async Task GetInventoryAsync_Sends_Refresh_True_When_Cache_Is_Bypassed()
    {
        // T123 — the sidecar has accepted ?refresh=true since T120 but nothing
        // on this side ever set it, so every backend read came out of the
        // 120-second cache. 07 §7.6a needs an uncached read.
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req =>
        {
            captured = req;
            return OkJson("{\"items\":[],\"totalCount\":0,\"tradeableCount\":0}");
        });
        var sut = BuildClient(handler);

        await sut.GetInventoryAsync(SteamId, bypassCache: true, CancellationToken.None);

        Assert.EndsWith($"/api/inventory/{SteamId}?refresh=true", captured!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetInventoryAsync_Omits_The_Refresh_Flag_On_The_Cached_Path()
    {
        // Omitted rather than `refresh=false`: the sidecar's own parser treats
        // an absent flag as "use the cache" (routes.ts parseRefreshParam), so
        // the ordinary read keeps its pre-T123 wire shape byte for byte.
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req =>
        {
            captured = req;
            return OkJson("{\"items\":[],\"totalCount\":0,\"tradeableCount\":0}");
        });
        var sut = BuildClient(handler);

        await sut.GetInventoryAsync(SteamId, bypassCache: false, CancellationToken.None);

        Assert.DoesNotContain("refresh", captured!.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetInventoryAsync_Returns_InventoryPrivate_On_422()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = JsonContent.Create(new { code = "INVENTORY_PRIVATE", error = "private" }),
        });
        var sut = BuildClient(handler);

        var result = await sut.GetInventoryAsync(SteamId, bypassCache: false, CancellationToken.None);

        Assert.Equal(SteamSidecarStatus.InventoryPrivate, result.Status);
        Assert.Null(result.Inventory);
    }

    [Fact]
    public async Task GetInventoryAsync_Returns_Unavailable_On_5xx()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var sut = BuildClient(handler);

        var result = await sut.GetInventoryAsync(SteamId, bypassCache: false, CancellationToken.None);

        Assert.Equal(SteamSidecarStatus.Unavailable, result.Status);
        Assert.Null(result.Inventory);
    }

    [Fact]
    public async Task GetInventoryAsync_Returns_Unavailable_On_TransportError()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var sut = BuildClient(handler);

        var result = await sut.GetInventoryAsync(SteamId, bypassCache: false, CancellationToken.None);

        Assert.Equal(SteamSidecarStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task InvalidateInventoryAsync_Sends_DELETE_With_Cache_Suffix()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var sut = BuildClient(handler);

        var result = await sut.InvalidateInventoryAsync(SteamId, CancellationToken.None);

        Assert.Equal(SteamSidecarStatus.Success, result);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Delete, captured!.Method);
        Assert.EndsWith($"/api/inventory/{SteamId}/cache", captured.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task InvalidateInventoryAsync_Swallows_Transport_Errors()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var sut = BuildClient(handler);

        var result = await sut.InvalidateInventoryAsync(SteamId, CancellationToken.None);

        Assert.Equal(SteamSidecarStatus.Unavailable, result);
    }

    [Fact]
    public async Task ISteamInventoryCacheInvalidator_Bridges_To_InvalidateInventoryAsync()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        ISteamInventoryCacheInvalidator port = BuildClient(handler);

        await port.InvalidateAsync(SteamId, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Delete, captured!.Method);
    }

    // ---------- helpers ----------

    private static HttpResponseMessage OkJson(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpSteamSidecarInventoryClient BuildClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://sidecar.test/"),
        };
        var options = Options.Create(new SteamSidecarOptions
        {
            BaseUrl = "http://sidecar.test",
            InternalKey = InternalKey,
            TimeoutSeconds = 30,
        });
        return new HttpSteamSidecarInventoryClient(
            http, options, NullLogger<HttpSteamSidecarInventoryClient>.Instance);
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
