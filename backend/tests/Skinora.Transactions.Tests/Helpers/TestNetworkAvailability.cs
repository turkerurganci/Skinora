using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Networks;

namespace Skinora.Transactions.Tests.Helpers;

/// <summary>
/// T147 — the production <see cref="BlockchainNetworkAvailability"/> (T148)
/// over a <see cref="FakeBlockchainServiceRegistry"/> and a settable health
/// reading: a network is open when its service is configured and it is not in
/// a health freeze (05 §3.3a, 11 §6.2 FK8-6).
/// </summary>
public static class TestNetworkAvailability
{
    /// <summary>Only TRON has a service — today's production answer until T150.</summary>
    public static IBlockchainNetworkAvailability TronOnly() => Over(BlockchainNetwork.TRON);

    /// <summary>Both networks have a (stubbed) service and are healthy.</summary>
    public static IBlockchainNetworkAvailability All() =>
        Over(BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM);

    /// <summary>Both networks have a service; <paramref name="frozen"/> is in a detected outage.</summary>
    public static IBlockchainNetworkAvailability AllWithFrozen(BlockchainNetwork frozen) =>
        new BlockchainNetworkAvailability(Registry(BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM), new Frozen(frozen));

    private static IBlockchainNetworkAvailability Over(params BlockchainNetwork[] configured) =>
        new BlockchainNetworkAvailability(Registry(configured), AlwaysHealthyBlockchainNetworks.Instance);

    private static FakeBlockchainServiceRegistry Registry(params BlockchainNetwork[] configured)
    {
        var registry = new FakeBlockchainServiceRegistry();
        foreach (var network in configured) registry.With(network);
        return registry;
    }

    private sealed class Frozen(BlockchainNetwork network) : IBlockchainNetworkHealth
    {
        public bool IsDegraded(BlockchainNetwork candidate) => candidate == network;
    }
}
