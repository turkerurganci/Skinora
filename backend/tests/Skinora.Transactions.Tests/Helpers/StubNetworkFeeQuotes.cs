using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;

namespace Skinora.Transactions.Tests.Helpers;

/// <summary>
/// T149 — fixed <see cref="INetworkFeeQuoteService"/> for suites that test
/// something else than the fee line. Every network answers 0 unless a test
/// sets an amount, so pre-T149 totals (price + commission) are unchanged.
/// Calls are recorded.
/// </summary>
public sealed class StubNetworkFeeQuotes : INetworkFeeQuoteService
{
    private readonly Dictionary<BlockchainNetwork, decimal> _amounts = new();

    public List<BlockchainNetwork> Requests { get; } = [];

    public StubNetworkFeeQuotes With(BlockchainNetwork network, decimal amount)
    {
        _amounts[network] = amount;
        return this;
    }

    public Task<NetworkFeeQuote> GetBuyerNetworkFeeAsync(
        BlockchainNetwork network, StablecoinType token, CancellationToken cancellationToken)
    {
        Requests.Add(network);
        var amount = _amounts.TryGetValue(network, out var a) ? a : 0m;
        return Task.FromResult(new NetworkFeeQuote(
            network, amount, amount == 0m ? NetworkFeeSource.NotCharged : NetworkFeeSource.RuntimeEstimate));
    }
}
