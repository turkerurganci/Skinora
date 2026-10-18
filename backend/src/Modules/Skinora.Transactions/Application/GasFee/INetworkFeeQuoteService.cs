using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.GasFee;

/// <summary>
/// T149 — the buyer's network fee line on a network (11 §F8 K8, 02 §5,
/// 08 §3.1a): what <c>networkQuotes</c> shows before the buyer picks a
/// network (07 §7.5) and what acceptance fixes on the transaction (07 §7.6).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Ethereum — always <c>0</c> (<see cref="NetworkFeeSource.NotCharged"/>):
///   the platform absorbs Ethereum's fees up to the cap.</item>
///   <item>TRON — <c>CEIL₂(sweepCostSun / 10⁶ × TRX price × (1 + buffer))</c>
///   from the network service's sweep estimate; the per-network fallback when
///   the estimate is unavailable, or when it exceeds the network's
///   <c>max_charged_gas_fee_usdt</c> sanity ceiling (refused, not clamped).</item>
/// </list>
/// Never throws for an estimator outage and never blocks: an answer is always
/// returned, so acceptance cannot wait on the estimate.
/// </remarks>
public interface INetworkFeeQuoteService
{
    Task<NetworkFeeQuote> GetBuyerNetworkFeeAsync(
        BlockchainNetwork network, StablecoinType token, CancellationToken cancellationToken);
}

/// <summary>Where a <see cref="NetworkFeeQuote"/> came from.</summary>
public enum NetworkFeeSource
{
    /// <summary>The network charges the buyer no line (Ethereum).</summary>
    NotCharged,

    /// <summary>Computed from the network service's live sweep estimate.</summary>
    RuntimeEstimate,

    /// <summary>The estimate was unavailable — the per-network fallback setting.</summary>
    StaticFallback,

    /// <summary>
    /// An estimate came back above the sanity ceiling and was not believed —
    /// the fallback was used and an error logged (operator signal).
    /// </summary>
    EstimateRejected,
}

/// <param name="Network">The network the line is for.</param>
/// <param name="Amount">The line, whole-cent (rounded UP on TRON); 0 when not charged.</param>
/// <param name="Source">How it was obtained.</param>
public sealed record NetworkFeeQuote(BlockchainNetwork Network, decimal Amount, NetworkFeeSource Source);
