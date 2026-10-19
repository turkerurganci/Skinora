using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skinora.API.Configuration;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.API.Tests.Unit.Configuration;

/// <summary>
/// T148 (05 §3.3a "Servisler") — the backend talks to each network's service
/// separately, and a network without a service fails closed on its own. Pinned
/// where the container builds the registry, because that is where a wrong
/// section name, options name or HTTP client name would hide: each network's
/// calls must reach that network's address with that network's key.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BlockchainServiceRegistryWiringTests
{
    private const string TronUrl = "http://tron-service:5200";
    private const string EthereumUrl = "http://ethereum-service:5300";

    [Fact]
    public async Task Each_Network_Reaches_Its_Own_Service_With_Its_Own_Key_And_Network()
    {
        var capture = new CapturingHandler();
        using var provider = Build(capture, ethereum: true);
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IBlockchainServiceRegistry>();

        Assert.Equal([BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM], registry.ConfiguredNetworks);

        await registry.Sidecar(BlockchainNetwork.TRON).StopMonitoringAsync("TAddr", default);
        await registry.Sidecar(BlockchainNetwork.ETHEREUM).StopMonitoringAsync("0xaddr", default);
        await registry.Transfers(BlockchainNetwork.ETHEREUM).GetStatusAsync("0xhash", default);
        await registry.GasFee(BlockchainNetwork.ETHEREUM).EstimateFeeUsdtAsync(
            new Skinora.Transactions.Application.GasFee.GasFeeEstimateRequest(null, "0xto", 1m, StablecoinType.USDT),
            default);

        var tron = Assert.Single(capture.Requests, r => r.Host == "tron-service");
        Assert.Equal(5200, tron.Port);
        Assert.Equal("tron-key", tron.InternalKey);
        Assert.Contains("\"network\":\"TRON\"", tron.Body);

        var eth = capture.Requests.Where(r => r.Host == "ethereum-service").ToList();
        Assert.Equal(3, eth.Count);
        Assert.All(eth, r => Assert.Equal("ethereum-key", r.InternalKey));
        Assert.Contains(eth, r => r.Path == "/api/monitor/stop" && r.Body!.Contains("\"network\":\"ETHEREUM\""));
        Assert.Contains(eth, r => r.Path == "/api/transfer/status/0xhash");
        Assert.Contains(eth, r => r.Path == "/api/transfer/estimate-fee" && r.Body!.Contains("\"network\":\"ETHEREUM\""));
    }

    [Fact]
    public async Task An_Unconfigured_Network_Fails_Closed_Sends_Nothing_And_Leaves_The_Other_Working()
    {
        // The state until T150: no EthereumSidecar section at all.
        var capture = new CapturingHandler();
        using var provider = Build(capture, ethereum: false);
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IBlockchainServiceRegistry>();

        Assert.Equal([BlockchainNetwork.TRON], registry.ConfiguredNetworks);
        Assert.False(registry.IsConfigured(BlockchainNetwork.ETHEREUM));

        var eth = registry.Sidecar(BlockchainNetwork.ETHEREUM);
        Assert.IsType<UnconfiguredBlockchainService>(eth);
        Assert.Equal(BlockchainSidecarStatus.NotConfigured,
            await eth.StartMonitoringAsync(new PaymentMonitorStartRequest(
                "0xaddr", Guid.NewGuid(), Guid.NewGuid(), "0xdac17f958d2ee523a2206206994597c13d831ec7", "USDT",
                PaymentMonitorCadence.Payment), default));
        var broadcast = await registry.Transfers(BlockchainNetwork.ETHEREUM).BroadcastAsync(
            new TransferBroadcastRequest(Guid.NewGuid(), BlockchainTransactionType.SELLER_PAYOUT,
                StablecoinType.USDT, 1m, "0xseller", null, null), default);
        Assert.Equal(TransferBroadcastStatus.TransientFailure, broadcast.Status);
        Assert.Equal(UnconfiguredBlockchainService.ErrorCode, broadcast.ErrorCode);
        Assert.Null(await registry.GasFee(BlockchainNetwork.ETHEREUM).EstimateFeeUsdtAsync(
            new Skinora.Transactions.Application.GasFee.GasFeeEstimateRequest(null, "0xto", 1m, StablecoinType.USDT),
            default));

        Assert.Empty(capture.Requests);

        // TRON is untouched by Ethereum's absence.
        Assert.Equal(BlockchainSidecarStatus.Success,
            await registry.Sidecar(BlockchainNetwork.TRON).StopMonitoringAsync("TAddr", default));
        Assert.Single(capture.Requests);
    }

    [Fact]
    public void Ethereum_Clients_Get_Their_Own_Budgets()
    {
        using var provider = Build(new CapturingHandler(), ethereum: true, ethereumTimeoutSeconds: "7");
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(TimeSpan.FromSeconds(7),
            factory.CreateClient(BlockchainHttpClientNames.Sidecar(BlockchainNetwork.ETHEREUM)).Timeout);
        Assert.Equal(TimeSpan.FromSeconds(21),
            factory.CreateClient(BlockchainHttpClientNames.GasFee(BlockchainNetwork.ETHEREUM)).Timeout);
        Assert.Equal(TimeSpan.FromSeconds(BlockchainSidecarOptions.DefaultTransferTimeoutSeconds),
            factory.CreateClient(BlockchainHttpClientNames.Transfer(BlockchainNetwork.ETHEREUM)).Timeout);
        // T158 doğrulaması (i) — the balance snapshot runs on the chain-read budget.
        Assert.Equal(TimeSpan.FromSeconds(21),
            factory.CreateClient(BlockchainHttpClientNames.SidecarChainRead(BlockchainNetwork.ETHEREUM)).Timeout);
        // TRON keeps its pre-F8 client names.
        Assert.Equal("BlockchainSidecar", BlockchainHttpClientNames.Sidecar(BlockchainNetwork.TRON));
        Assert.Equal("BlockchainSidecar.ETHEREUM", BlockchainHttpClientNames.Sidecar(BlockchainNetwork.ETHEREUM));
        Assert.Equal("BlockchainSidecar.ChainRead.ETHEREUM",
            BlockchainHttpClientNames.SidecarChainRead(BlockchainNetwork.ETHEREUM));
    }

    /// <summary>
    /// T158 doğrulaması, bulgu (i) — an Ethereum balance chunk is paced on the
    /// service through the RPC budget it shares with the payment scanner (up to
    /// ≈ 16.5 s for 10 addresses — <c>EthereumBalanceSnapshotBudgetTests</c>),
    /// so the balances request must run under the chain-read budget, while
    /// derive / monitor calls keep the short one. Measured where the container
    /// builds the clients, against a service that answers late: with
    /// <c>TimeoutSeconds</c> 1 the wallet budget is 1 s and the chain-read
    /// budget 3 s, and the service answers after 2 s.
    /// </summary>
    [Fact]
    public async Task T158i_Ethereum_Balances_Run_Under_The_Chain_Read_Budget_And_Monitor_Calls_Keep_The_Short_One()
    {
        var slow = new LateAnsweringHandler(TimeSpan.FromSeconds(2));
        using var provider = Build(slow, ethereum: true, ethereumTimeoutSeconds: "1");
        using var scope = provider.CreateScope();
        var sidecar = scope.ServiceProvider.GetRequiredService<IBlockchainServiceRegistry>()
            .Sidecar(BlockchainNetwork.ETHEREUM);

        var balances = await sidecar.GetWalletBalancesAsync(["0xaddr"], default);
        var stop = await sidecar.StopMonitoringAsync("0xaddr", default);

        Assert.Equal(BlockchainSidecarStatus.Success, balances.Status);
        Assert.Equal("0xaddr", Assert.Single(balances.Balances!).Address);
        Assert.Equal(BlockchainSidecarStatus.Unavailable, stop);
        Assert.Equal(["/api/wallet/balances", "/api/monitor/stop"], slow.Paths);
    }

    private static ServiceProvider Build(
        HttpMessageHandler handler, bool ethereum, string ethereumTimeoutSeconds = "10")
    {
        var settings = new Dictionary<string, string?>
        {
            ["BlockchainSidecar:BaseUrl"] = TronUrl,
            ["BlockchainSidecar:InternalKey"] = "tron-key",
        };
        if (ethereum)
        {
            settings["EthereumSidecar:BaseUrl"] = EthereumUrl;
            settings["EthereumSidecar:InternalKey"] = "ethereum-key";
            settings["EthereumSidecar:TimeoutSeconds"] = ethereumTimeoutSeconds;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransactionsModule(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        foreach (var network in Enum.GetValues<BlockchainNetwork>())
        {
            services.AddHttpClient(BlockchainHttpClientNames.Sidecar(network)).ConfigurePrimaryHttpMessageHandler(() => handler);
            services.AddHttpClient(BlockchainHttpClientNames.SidecarChainRead(network)).ConfigurePrimaryHttpMessageHandler(() => handler);
            services.AddHttpClient(BlockchainHttpClientNames.Transfer(network)).ConfigurePrimaryHttpMessageHandler(() => handler);
            services.AddHttpClient(BlockchainHttpClientNames.GasFee(network)).ConfigurePrimaryHttpMessageHandler(() => handler);
        }

        return services.BuildServiceProvider();
    }

    private sealed record Captured(string Host, int Port, string Path, string? InternalKey, string? Body);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<Captured> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Captured(
                request.RequestUri!.Host,
                request.RequestUri.Port,
                request.RequestUri.AbsolutePath,
                request.Headers.TryGetValues("X-Internal-Key", out var keys) ? keys.Single() : null,
                body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>
    /// A service that answers every call after <c>delay</c>. The wait honours
    /// the request's token, which is how <see cref="HttpClient.Timeout"/>
    /// reaches the handler — a call whose budget is shorter is cancelled.
    /// </summary>
    private sealed class LateAnsweringHandler(TimeSpan delay) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            await Task.Delay(delay, cancellationToken);
            var body = request.RequestUri.AbsolutePath == "/api/wallet/balances"
                ? """{"blockNumber":1000,"balances":[{"address":"0xaddr","tokens":{"USDT":"0","USDC":"0","ETH":"0"}}]}"""
                : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
