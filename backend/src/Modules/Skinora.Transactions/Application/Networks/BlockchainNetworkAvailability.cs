using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Networks;

/// <summary>
/// Which networks are open for a new transaction right now (07 §1.3
/// <c>supportedNetworks</c>, 05 §3.3a): a network needs a configured service
/// and must not be in a health freeze.
/// </summary>
public interface IBlockchainNetworkAvailability
{
    /// <summary>Open networks, in enum order (TRON first).</summary>
    IReadOnlyList<BlockchainNetwork> OpenForNewTransactions();

    /// <summary>True when <paramref name="network"/> is open for a new transaction.</summary>
    bool IsOpenForNewTransactions(BlockchainNetwork network);
}
