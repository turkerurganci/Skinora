using Skinora.Shared.Enums;

namespace Skinora.Transactions.Domain.Calculations;

/// <summary>
/// T149 — who pays which network fee, per network (11 §F8 K4/K8, 02 §4.7,
/// 08 §3.1a, §3.7.7). Business rules the owner decided, not chain facts — which
/// is why they live here and not in <c>BlockchainNetworkPolicy</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>TRON</b> — the buyer pays the deposit → hot wallet sweep as a
///   separate "TRON network fee" line fixed at acceptance; the seller pays the
///   payout through the gas-protection rule (the platform absorbs up to
///   <c>gas_fee_protection_ratio</c> of the commission, the rest comes off the
///   seller's amount).</item>
///   <item><b>Ethereum</b> — no line for the buyer, nothing off the seller: the
///   platform absorbs sweep and payout up to the per-transfer fee cap
///   (<c>blockchain.ethereum.fee_cap_usd_per_transfer</c>); above the cap the
///   transfer waits (T153 / T158), it is never charged to a user.</item>
/// </list>
/// Refunds are the same on both networks: the measured refund fee comes off
/// the refund (02 §4.6).
/// </remarks>
public static class NetworkFeeRules
{
    /// <summary>True when the buyer pays a network fee line on <paramref name="network"/>.</summary>
    public static bool BuyerPaysNetworkFeeLine(BlockchainNetwork network) => network switch
    {
        BlockchainNetwork.TRON => true,
        BlockchainNetwork.ETHEREUM => false,
        _ => throw new ArgumentOutOfRangeException(nameof(network), network, "No fee rule is defined for this network."),
    };

    /// <summary>
    /// True when the payout fee above the platform's share of the commission
    /// comes off the seller's amount on <paramref name="network"/> (02 §4.7).
    /// </summary>
    public static bool SellerBearsPayoutFeeOverage(BlockchainNetwork network) => network switch
    {
        BlockchainNetwork.TRON => true,
        BlockchainNetwork.ETHEREUM => false,
        _ => throw new ArgumentOutOfRangeException(nameof(network), network, "No fee rule is defined for this network."),
    };
}
