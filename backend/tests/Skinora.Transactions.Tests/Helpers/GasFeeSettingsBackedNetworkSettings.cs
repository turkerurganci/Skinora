using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Networks;

namespace Skinora.Transactions.Tests.Helpers;

/// <summary>
/// T149 — <see cref="RefundDecisionService"/> reads the minimum-refund
/// multiplier per network from <see cref="IBlockchainNetworkSettingsProvider"/>.
/// The pre-T149 tests arrange that multiplier through their
/// <see cref="IGasFeeSettingsProvider"/> stub; this adapter answers every
/// network with its seeded defaults and that stub's multiplier, read on every
/// call, so those tests keep their meaning unchanged.
/// </summary>
public sealed class GasFeeSettingsBackedNetworkSettings : IBlockchainNetworkSettingsProvider
{
    private readonly IGasFeeSettingsProvider _gasFee;

    public GasFeeSettingsBackedNetworkSettings(IGasFeeSettingsProvider gasFee)
    {
        _gasFee = gasFee;
    }

    public async Task<BlockchainNetworkSettings> GetAsync(
        BlockchainNetwork network, CancellationToken cancellationToken)
    {
        var legacy = await _gasFee.GetAsync(cancellationToken);
        return BlockchainNetworkSettingsProvider.DefaultsFor(network) with
        {
            MinRefundThresholdRatio = legacy.MinRefundThresholdRatio,
        };
    }

    /// <summary>The service as the pre-T149 tests built it, around <paramref name="gasFee"/>.</summary>
    public static RefundDecisionService NewRefundDecisionService(IGasFeeSettingsProvider gasFee) =>
        new(gasFee, new GasFeeSettingsBackedNetworkSettings(gasFee));
}
