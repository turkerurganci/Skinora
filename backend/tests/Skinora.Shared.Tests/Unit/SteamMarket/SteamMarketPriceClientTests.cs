using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Shared.SteamMarket;
using Xunit;

namespace Skinora.Shared.Tests.Unit.SteamMarket;

public class SteamMarketPriceClientTests
{
    private static SteamMarketSettings Settings() => new()
    {
        Provider = SteamMarketSettings.ProviderSteamMarket,
        BaseUrl = "https://market.example",
        AppId = 730,
        Currency = 1,
        TimeoutSeconds = 5,
        RateLimitPerMinute = 20,
    };

    [Fact]
    public async Task GetPriceAsync_OkMedian_ParsesAndAcquires()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK,
             """{"success":true,"median_price":"$13.10","lowest_price":"$12.50"}""",
             null));
        var limiter = new SpyRateLimiter();
        var sut = BuildClient(handler, limiter);

        var quote = await sut.GetPriceAsync("AK-47 | Redline (Field-Tested)", default);

        Assert.False(quote.IsNoPrice);
        Assert.Equal(13.10m, quote.MedianPrice);
        Assert.Equal(12.50m, quote.LowestPrice);
        Assert.Equal(1, limiter.AcquireCount);
    }

    [Fact]
    public async Task GetPriceAsync_BuildsCanonicalUrl()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, """{"success":true,"median_price":"$1"}""", null));
        var sut = BuildClient(handler, new SpyRateLimiter());

        await sut.GetPriceAsync("AK-47 | Redline (Field-Tested)", default);

        var uri = handler.Requests.Single().Uri;
        Assert.Equal("/market/priceoverview/", uri.AbsolutePath);
        Assert.Contains("appid=730", uri.Query, StringComparison.Ordinal);
        Assert.Contains("currency=1", uri.Query, StringComparison.Ordinal);
        // The pipe + parens must be URL-encoded per 08 §7.2 (the doc shows
        // %20 and %7C in the sample request).
        Assert.Contains("market_hash_name=AK-47%20%7C%20Redline%20%28Field-Tested%29", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPriceAsync_SuccessFalse_ReturnsNoPriceWithoutThrowing()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, """{"success":false}""", null));
        var sut = BuildClient(handler, new SpyRateLimiter());

        var quote = await sut.GetPriceAsync("Some Item", default);

        Assert.True(quote.IsNoPrice);
    }

    [Fact]
    public async Task GetPriceAsync_SuccessTrueButEmptyPrices_ReturnsNoPrice()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK,
             """{"success":true,"median_price":"","lowest_price":""}""",
             null));
        var sut = BuildClient(handler, new SpyRateLimiter());

        var quote = await sut.GetPriceAsync("Rare Item", default);

        Assert.True(quote.IsNoPrice);
    }

    [Fact]
    public async Task GetPriceAsync_429WithRetryAfter_ThrowsRateLimitedAndRegisters()
    {
        var handler = new SequenceHandler(
            ((HttpStatusCode)429, """{"error":"rate"}""",
             new[] { ("Retry-After", "45") }));
        var limiter = new SpyRateLimiter();
        var sut = BuildClient(handler, limiter);

        var ex = await Assert.ThrowsAsync<SteamMarketRateLimitedException>(() =>
            sut.GetPriceAsync("X", default));
        Assert.Equal(TimeSpan.FromSeconds(45), ex.RetryAfter);
        Assert.Single(limiter.RetryAfterCalls);
        Assert.Equal(TimeSpan.FromSeconds(45), limiter.RetryAfterCalls[0]);
    }

    [Fact]
    public async Task GetPriceAsync_429WithoutRetryAfter_DefaultsTo30s()
    {
        var handler = new SequenceHandler(
            ((HttpStatusCode)429, "{}", null));
        var limiter = new SpyRateLimiter();
        var sut = BuildClient(handler, limiter);

        await Assert.ThrowsAsync<SteamMarketRateLimitedException>(() =>
            sut.GetPriceAsync("X", default));
        Assert.Equal(TimeSpan.FromSeconds(30), limiter.RetryAfterCalls.Single());
    }

    [Fact]
    public async Task GetPriceAsync_5xx_ThrowsTransient()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.InternalServerError, "{}", null));
        var sut = BuildClient(handler, new SpyRateLimiter());

        var ex = await Assert.ThrowsAsync<SteamMarketTransientException>(() =>
            sut.GetPriceAsync("X", default));
        Assert.Equal(500, ex.StatusCode);
    }

    [Fact]
    public async Task GetPriceAsync_4xxOther_ThrowsPermanent()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.BadRequest, "{}", null));
        var sut = BuildClient(handler, new SpyRateLimiter());

        var ex = await Assert.ThrowsAsync<SteamMarketPermanentException>(() =>
            sut.GetPriceAsync("X", default));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task GetPriceAsync_HttpRequestException_ThrowsTransient()
    {
        var sut = BuildClient(new ThrowingHandler(new HttpRequestException("dns")), new SpyRateLimiter());

        await Assert.ThrowsAsync<SteamMarketTransientException>(() =>
            sut.GetPriceAsync("X", default));
    }

    [Fact]
    public async Task GetPriceAsync_TimeoutNotCancellation_ThrowsTransient()
    {
        // TaskCanceledException with a non-cancelled CT signals a timeout.
        var sut = BuildClient(new ThrowingHandler(new TaskCanceledException("timeout")), new SpyRateLimiter());

        await Assert.ThrowsAsync<SteamMarketTransientException>(() =>
            sut.GetPriceAsync("X", default));
    }

    [Fact]
    public async Task GetPriceAsync_InvalidJson_ThrowsPermanent()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, "not json", null));
        var sut = BuildClient(handler, new SpyRateLimiter());

        var ex = await Assert.ThrowsAsync<SteamMarketPermanentException>(() =>
            sut.GetPriceAsync("X", default));
        Assert.Equal(200, ex.StatusCode);
    }

    [Fact]
    public async Task GetPriceAsync_EmptyName_Throws()
    {
        var sut = BuildClient(new SequenceHandler(), new SpyRateLimiter());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.GetPriceAsync("  ", default));
    }

    [Fact]
    public void Constructor_MissingBaseUrl_Throws()
    {
        var settings = Settings();
        settings.BaseUrl = string.Empty;

        Assert.Throws<InvalidOperationException>(() =>
            new SteamMarketPriceClient(
                new HttpClient(new SequenceHandler()),
                new SpyRateLimiter(),
                Options.Create(settings),
                NullLogger<SteamMarketPriceClient>.Instance));
    }

    // --- helpers ---

    private static SteamMarketPriceClient BuildClient(HttpMessageHandler handler, ISteamMarketRateLimiter limiter)
    {
        var http = new HttpClient(handler);
        return new SteamMarketPriceClient(
            http,
            limiter,
            Options.Create(Settings()),
            NullLogger<SteamMarketPriceClient>.Instance);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();
        private readonly Queue<(HttpStatusCode Status, string Body, (string Name, string Value)[]? Headers)> _queue = new();

        public SequenceHandler(params (HttpStatusCode Status, string Body, (string Name, string Value)[]? Headers)[] responses)
        {
            foreach (var r in responses) _queue.Enqueue(r);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.RequestUri!));

            if (_queue.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            var (status, body, headers) = _queue.Dequeue();
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            if (headers is not null)
            {
                foreach (var (name, value) in headers)
                {
                    response.Headers.TryAddWithoutValidation(name, value);
                }
            }

            return Task.FromResult(response);
        }
    }

    private sealed record RecordedRequest(Uri Uri);

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;
        public ThrowingHandler(Exception ex) => _ex = ex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _ex;
    }

    private sealed class SpyRateLimiter : ISteamMarketRateLimiter
    {
        public int AcquireCount { get; private set; }
        public List<TimeSpan> RetryAfterCalls { get; } = new();

        public Task AcquireAsync(CancellationToken cancellationToken = default)
        {
            AcquireCount++;
            return Task.CompletedTask;
        }

        public void RegisterRetryAfter(TimeSpan retryAfter)
        {
            RetryAfterCalls.Add(retryAfter);
        }
    }
}
