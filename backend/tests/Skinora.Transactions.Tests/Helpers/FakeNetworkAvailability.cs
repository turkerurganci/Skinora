using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Networks;

namespace Skinora.Transactions.Tests.Helpers;

/// <summary>
/// Test double for <see cref="IBlockchainNetworkAvailability"/> (T147): the
/// networks passed in are open for new transactions, every other one is
/// closed. Default: TRON only — today's production answer until T150.
/// </summary>
public sealed class FakeNetworkAvailability : IBlockchainNetworkAvailability
{
    private readonly HashSet<BlockchainNetwork> _open;

    public FakeNetworkAvailability(params BlockchainNetwork[] open)
    {
        _open = open.Length == 0 ? [BlockchainNetwork.TRON] : [.. open];
    }

    /// <summary>Both networks open — Ethereum with a (stubbed) service.</summary>
    public static FakeNetworkAvailability All() =>
        new(BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM);

    public IReadOnlyList<BlockchainNetwork> OpenForNewTransactions() =>
        Enum.GetValues<BlockchainNetwork>().Where(_open.Contains).ToList();

    public bool IsOpenForNewTransactions(BlockchainNetwork network) => _open.Contains(network);

    public void Close(BlockchainNetwork network) => _open.Remove(network);
}
