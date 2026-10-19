using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.Transactions.Application.Networks;

/// <summary>
/// Default <see cref="IBlockchainServiceRegistry"/> — builds each network's
/// HTTP clients from that network's <see cref="BlockchainSidecarOptions"/>
/// instance and named <see cref="HttpClient"/>s
/// (<see cref="BlockchainHttpClientNames"/>). Scoped, like the typed clients it
/// replaces; the underlying handlers are pooled by <see cref="IHttpClientFactory"/>.
/// </summary>
public sealed class BlockchainServiceRegistry : IBlockchainServiceRegistry
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<BlockchainSidecarOptions> _options;
    private readonly ILoggerFactory _loggerFactory;

    private readonly Dictionary<BlockchainNetwork, IBlockchainSidecarClient> _sidecars = new();
    private readonly Dictionary<BlockchainNetwork, IBlockchainTransferClient> _transfers = new();
    private readonly Dictionary<BlockchainNetwork, IGasFeeEstimator> _estimators = new();

    public BlockchainServiceRegistry(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<BlockchainSidecarOptions> options,
        ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _loggerFactory = loggerFactory;
    }

    public IReadOnlyList<BlockchainNetwork> ConfiguredNetworks =>
        Enum.GetValues<BlockchainNetwork>().Where(IsConfigured).ToList();

    public bool IsConfigured(BlockchainNetwork network) => OptionsFor(network).IsConfigured;

    public IBlockchainSidecarClient Sidecar(BlockchainNetwork network) =>
        GetOrCreate(_sidecars, network, options => new HttpBlockchainSidecarClient(
            network,
            _httpClientFactory.CreateClient(BlockchainHttpClientNames.Sidecar(network)),
            _httpClientFactory.CreateClient(BlockchainHttpClientNames.SidecarChainRead(network)),
            options,
            _loggerFactory.CreateLogger<HttpBlockchainSidecarClient>()));

    public IBlockchainTransferClient Transfers(BlockchainNetwork network) =>
        GetOrCreate(_transfers, network, options => new HttpBlockchainTransferClient(
            network,
            _httpClientFactory.CreateClient(BlockchainHttpClientNames.Transfer(network)),
            options,
            _loggerFactory.CreateLogger<HttpBlockchainTransferClient>()));

    public IGasFeeEstimator GasFee(BlockchainNetwork network) =>
        GetOrCreate(_estimators, network, options => new HttpSidecarGasFeeEstimator(
            network,
            _httpClientFactory.CreateClient(BlockchainHttpClientNames.GasFee(network)),
            options,
            _loggerFactory.CreateLogger<HttpSidecarGasFeeEstimator>()));

    private BlockchainSidecarOptions OptionsFor(BlockchainNetwork network) =>
        _options.Get(BlockchainSidecarOptions.OptionsNameFor(network));

    private T GetOrCreate<T>(
        Dictionary<BlockchainNetwork, T> cache,
        BlockchainNetwork network,
        Func<BlockchainSidecarOptions, T> create)
        where T : class
    {
        if (cache.TryGetValue(network, out var existing)) return existing;

        var options = OptionsFor(network);
        T client = options.IsConfigured
            ? create(options)
            : (T)(object)new UnconfiguredBlockchainService(
                network, _loggerFactory.CreateLogger<UnconfiguredBlockchainService>());
        cache[network] = client;
        return client;
    }
}
