using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.Transactions.Tests.Helpers;

/// <summary>
/// Test stand-in for <see cref="IBlockchainServiceRegistry"/> (T148). Each
/// network gets the fakes a test hands it; a network with none resolves to the
/// production fail-closed <see cref="UnconfiguredBlockchainService"/>, exactly
/// as an unconfigured network does in the real registry. Every resolution is
/// recorded so a test can assert which network's service a caller asked.
/// </summary>
public sealed class FakeBlockchainServiceRegistry : IBlockchainServiceRegistry
{
    private readonly Dictionary<BlockchainNetwork, IBlockchainSidecarClient> _sidecars = new();
    private readonly Dictionary<BlockchainNetwork, IBlockchainTransferClient> _transfers = new();
    private readonly Dictionary<BlockchainNetwork, IGasFeeEstimator> _estimators = new();
    private readonly HashSet<BlockchainNetwork> _configured = new();

    /// <summary>Every <c>Sidecar/Transfers/GasFee</c> call, in order: (kind, network).</summary>
    public List<(string Kind, BlockchainNetwork Network)> Requests { get; } = [];

    /// <summary>A registry whose only configured network is TRON, served by the given fakes.</summary>
    public static FakeBlockchainServiceRegistry ForTron(
        IBlockchainSidecarClient? sidecar = null,
        IBlockchainTransferClient? transfers = null,
        IGasFeeEstimator? gasFee = null) =>
        new FakeBlockchainServiceRegistry().With(BlockchainNetwork.TRON, sidecar, transfers, gasFee);

    /// <summary>Configure <paramref name="network"/> with the given fakes (any may be null).</summary>
    public FakeBlockchainServiceRegistry With(
        BlockchainNetwork network,
        IBlockchainSidecarClient? sidecar = null,
        IBlockchainTransferClient? transfers = null,
        IGasFeeEstimator? gasFee = null)
    {
        _configured.Add(network);
        if (sidecar is not null) _sidecars[network] = sidecar;
        if (transfers is not null) _transfers[network] = transfers;
        if (gasFee is not null) _estimators[network] = gasFee;
        return this;
    }

    public IReadOnlyList<BlockchainNetwork> ConfiguredNetworks =>
        Enum.GetValues<BlockchainNetwork>().Where(_configured.Contains).ToList();

    public bool IsConfigured(BlockchainNetwork network) => _configured.Contains(network);

    public IBlockchainSidecarClient Sidecar(BlockchainNetwork network)
    {
        Requests.Add(("Sidecar", network));
        return _sidecars.TryGetValue(network, out var client) ? client : Unconfigured(network);
    }

    public IBlockchainTransferClient Transfers(BlockchainNetwork network)
    {
        Requests.Add(("Transfers", network));
        return _transfers.TryGetValue(network, out var client) ? client : Unconfigured(network);
    }

    public IGasFeeEstimator GasFee(BlockchainNetwork network)
    {
        Requests.Add(("GasFee", network));
        return _estimators.TryGetValue(network, out var client) ? client : Unconfigured(network);
    }

    private static UnconfiguredBlockchainService Unconfigured(BlockchainNetwork network) =>
        new(network, NullLogger.Instance);
}
