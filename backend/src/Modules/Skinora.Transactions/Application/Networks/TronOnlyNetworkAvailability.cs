using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Networks;

/// <summary>
/// Interim <see cref="IBlockchainNetworkAvailability"/> until the per-network
/// service registry (T148) is on this branch: only TRON has a service.
/// </summary>
public sealed class TronOnlyNetworkAvailability : IBlockchainNetworkAvailability
{
    public IReadOnlyList<BlockchainNetwork> OpenForNewTransactions() => [BlockchainNetwork.TRON];

    public bool IsOpenForNewTransactions(BlockchainNetwork network) => network == BlockchainNetwork.TRON;
}
