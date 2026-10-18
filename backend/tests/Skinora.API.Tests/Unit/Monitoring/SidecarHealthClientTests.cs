using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.API.Monitoring;
using Skinora.Shared.Enums;
using Skinora.Steam.Application.Inventory;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.API.Tests.Unit.Monitoring;

/// <summary>
/// T151 (11 §F8 T151 "Kod düzeyi kapı") — the probe counts the Ethereum
/// service healthy only when its <c>/health</c> body says
/// <c>acceptsPayments: true</c>; TRON's service, which predates the flag, is
/// not asked for it. Before T151 a backend pointed at the T150 skeleton would
/// have opened Ethereum while <c>monitor/start</c> answered 501 and nobody
/// watched the deposits.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SidecarHealthClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static SidecarHealthClient Client(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.Configure<BlockchainSidecarOptions>(o => o.BaseUrl = "http://tron:5200");
        services.Configure<BlockchainSidecarOptions>(
            BlockchainSidecarOptions.OptionsNameFor(BlockchainNetwork.ETHEREUM),
            o => o.BaseUrl = "http://ethereum:5300");
        var provider = services.BuildServiceProvider();
        return new SidecarHealthClient(
            new Factory(handler),
            Options.Create(new SteamSidecarOptions()),
            provider.GetRequiredService<IOptionsMonitor<BlockchainSidecarOptions>>(),
            NullLogger<SidecarHealthClient>.Instance);
    }

    [Theory]
    [InlineData("""{"status":"healthy","network":"ETHEREUM","acceptsPayments":true}""", true)]
    [InlineData("""{"status":"healthy","network":"ETHEREUM","acceptsPayments":false}""", false)]
    [InlineData("""{"status":"healthy","network":"ETHEREUM"}""", false)]
    [InlineData("""{"status":"healthy","network":"ETHEREUM","acceptsPayments":"true"}""", false)]
    [InlineData("""{"status":"healthy","network":"TRON","acceptsPayments":true}""", false)]
    [InlineData("not json", false)]
    public async Task Ethereum_Is_Healthy_Only_With_AcceptsPayments_True(string body, bool expected)
    {
        var healthy = await Client(new StubHandler(HttpStatusCode.OK, body))
            .IsHealthyAsync(PlatformComponents.BlockchainEthereum, CancellationToken.None);

        Assert.Equal(expected, healthy);
    }

    [Fact]
    public async Task Ethereum_503_Is_Unhealthy_Whatever_The_Flag()
    {
        var healthy = await Client(new StubHandler(
                HttpStatusCode.ServiceUnavailable,
                """{"status":"unhealthy","network":"ETHEREUM","acceptsPayments":true}"""))
            .IsHealthyAsync(PlatformComponents.BlockchainEthereum, CancellationToken.None);

        Assert.False(healthy);
    }

    [Theory]
    [InlineData("""{"status":"healthy","network":"TRON"}""")]
    [InlineData("""{"status":"healthy"}""")]
    public async Task Tron_Is_Not_Asked_For_The_Flag(string body)
    {
        var healthy = await Client(new StubHandler(HttpStatusCode.OK, body))
            .IsHealthyAsync(PlatformComponents.BlockchainTron, CancellationToken.None);

        Assert.True(healthy);
    }

    [Fact]
    public void Only_Ethereum_Requires_The_Capability()
    {
        Assert.True(PlatformComponents.RequiresPaymentCapability(BlockchainNetwork.ETHEREUM));
        Assert.False(PlatformComponents.RequiresPaymentCapability(BlockchainNetwork.TRON));
    }
}
