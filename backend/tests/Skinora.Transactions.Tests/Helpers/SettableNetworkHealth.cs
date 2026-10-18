using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Networks;

namespace Skinora.Transactions.Tests.Helpers;

/// <summary>
/// T150 — a health reading a test can flip: the networks in
/// <see cref="Degraded"/> read as being in a detected outage (the platform
/// health probe's signal), every other network as healthy.
/// </summary>
public sealed class SettableNetworkHealth : IBlockchainNetworkHealth
{
    public HashSet<BlockchainNetwork> Degraded { get; } = new();

    public bool IsDegraded(BlockchainNetwork network) => Degraded.Contains(network);
}
