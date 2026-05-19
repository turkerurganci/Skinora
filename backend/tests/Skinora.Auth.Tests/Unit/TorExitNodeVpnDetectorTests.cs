using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Skinora.Auth.Application.SteamAuthentication;

namespace Skinora.Auth.Tests.Unit;

public class TorExitNodeVpnDetectorTests
{
    private const string Url = "https://test.torproject.local/torbulkexitlist";

    private static TorExitNodeVpnDetector Build(
        StubHttpHandler handler,
        FakeTimeProvider time,
        TimeSpan? cache = null,
        TimeSpan? timeout = null)
    {
        var settings = Options.Create(new VpnDetectionSettings
        {
            Enabled = true,
            TorExitListUrl = Url,
            CacheDurationMinutes = (int)(cache ?? TimeSpan.FromHours(1)).TotalMinutes,
            RefreshTimeoutSeconds = (int)(timeout ?? TimeSpan.FromSeconds(10)).TotalSeconds,
        });

        var httpClient = new HttpClient(handler);
        return new TorExitNodeVpnDetector(
            httpClient,
            settings,
            time,
            NullLogger<TorExitNodeVpnDetector>.Instance);
    }

    [Fact]
    public async Task IsVpnOrProxyAsync_NullOrInvalidIp_ReturnsFalseWithoutFetching()
    {
        var handler = new StubHttpHandler("");
        var detector = Build(handler, new FakeTimeProvider());

        Assert.False(await detector.IsVpnOrProxyAsync(null, default));
        Assert.False(await detector.IsVpnOrProxyAsync("", default));
        Assert.False(await detector.IsVpnOrProxyAsync("not-an-ip", default));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task IsVpnOrProxyAsync_IpOnList_ReturnsTrue()
    {
        var handler = new StubHttpHandler("1.2.3.4\n5.6.7.8\n");
        var detector = Build(handler, new FakeTimeProvider());

        Assert.True(await detector.IsVpnOrProxyAsync("1.2.3.4", default));
        Assert.True(await detector.IsVpnOrProxyAsync("5.6.7.8", default));
    }

    [Fact]
    public async Task IsVpnOrProxyAsync_IpNotOnList_ReturnsFalse()
    {
        var handler = new StubHttpHandler("1.2.3.4\n");
        var detector = Build(handler, new FakeTimeProvider());

        Assert.False(await detector.IsVpnOrProxyAsync("9.9.9.9", default));
    }

    [Fact]
    public async Task IsVpnOrProxyAsync_ListIgnoresCommentsAndBlankLines()
    {
        var handler = new StubHttpHandler("# This is a comment\n\n1.2.3.4\n   \n");
        var detector = Build(handler, new FakeTimeProvider());

        Assert.True(await detector.IsVpnOrProxyAsync("1.2.3.4", default));
    }

    [Fact]
    public async Task IsVpnOrProxyAsync_CacheHonored_DoesNotRefetchWithinWindow()
    {
        var handler = new StubHttpHandler("1.2.3.4\n");
        var time = new FakeTimeProvider();
        var detector = Build(handler, time, cache: TimeSpan.FromMinutes(60));

        Assert.True(await detector.IsVpnOrProxyAsync("1.2.3.4", default));
        Assert.True(await detector.IsVpnOrProxyAsync("1.2.3.4", default));
        Assert.Equal(1, handler.Calls);

        time.Advance(TimeSpan.FromMinutes(30));
        Assert.True(await detector.IsVpnOrProxyAsync("1.2.3.4", default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task IsVpnOrProxyAsync_CacheExpires_TriggersRefetch()
    {
        var handler = new StubHttpHandler("1.2.3.4\n");
        var time = new FakeTimeProvider();
        var detector = Build(handler, time, cache: TimeSpan.FromMinutes(60));

        Assert.True(await detector.IsVpnOrProxyAsync("1.2.3.4", default));
        Assert.Equal(1, handler.Calls);

        time.Advance(TimeSpan.FromMinutes(61));
        handler.NextBody = "9.9.9.9\n";
        Assert.False(await detector.IsVpnOrProxyAsync("1.2.3.4", default));
        Assert.True(await detector.IsVpnOrProxyAsync("9.9.9.9", default));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task IsVpnOrProxyAsync_HttpError_FailsSoftAndReturnsFalse()
    {
        var handler = new StubHttpHandler("", HttpStatusCode.ServiceUnavailable);
        var detector = Build(handler, new FakeTimeProvider());

        Assert.False(await detector.IsVpnOrProxyAsync("1.2.3.4", default));
    }

    [Fact]
    public async Task IsVpnOrProxyAsync_TransportException_FailsSoftAndReturnsFalse()
    {
        var handler = new StubHttpHandler("", throwOnSend: true);
        var detector = Build(handler, new FakeTimeProvider());

        Assert.False(await detector.IsVpnOrProxyAsync("1.2.3.4", default));
    }

    [Fact]
    public async Task IsVpnOrProxyAsync_RefetchFailsButCachePresent_KeepsServingCache()
    {
        var handler = new StubHttpHandler("1.2.3.4\n");
        var time = new FakeTimeProvider();
        var detector = Build(handler, time, cache: TimeSpan.FromMinutes(60));

        Assert.True(await detector.IsVpnOrProxyAsync("1.2.3.4", default));

        time.Advance(TimeSpan.FromMinutes(61));
        handler.ThrowOnSend = true;
        // Refresh fails but the previously parsed snapshot is still served
        // — better stale than locked out.
        Assert.True(await detector.IsVpnOrProxyAsync("1.2.3.4", default));
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        public StubHttpHandler(string body, HttpStatusCode status = HttpStatusCode.OK, bool throwOnSend = false)
        {
            NextBody = body;
            NextStatus = status;
            ThrowOnSend = throwOnSend;
        }

        public string NextBody { get; set; }
        public HttpStatusCode NextStatus { get; set; }
        public bool ThrowOnSend { get; set; }
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (ThrowOnSend) throw new HttpRequestException("simulated transport error");

            return Task.FromResult(new HttpResponseMessage(NextStatus)
            {
                Content = new StringContent(NextBody),
            });
        }
    }
}
