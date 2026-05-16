namespace Skinora.Transactions.Application.GasFee;

/// <summary>
/// Reads <c>gas_fee_protection_ratio</c>, <c>min_refund_threshold_ratio</c>
/// and <c>blockchain.refund_gas_fee_estimate_usdt</c> from SystemSettings
/// (02 §4.7, 09 §14.4, T72 — 08 §3.4). Mirrors
/// <c>IReputationThresholdsProvider</c> (T43) — caller treats every call as
/// cheap, no caching contract is exposed.
/// </summary>
public interface IGasFeeSettingsProvider
{
    Task<GasFeeSettings> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Snapshot of the gas-fee-related settings that drive
/// <c>RefundDecisionService</c> and the T72 amount validation pipeline.
/// </summary>
/// <param name="ProtectionRatio">
/// Fraction of the commission the platform is willing to absorb as gas fee
/// when paying out the seller (02 §4.7). Validator stage 2 enforces
/// <c>0 &lt; ratio &lt; 1</c>.
/// </param>
/// <param name="MinRefundThresholdRatio">
/// Multiplier against the gas fee that defines the minimum payable refund
/// floor (09 §14.4). Validator stage 2 enforces <c>ratio &gt; 0</c>; the
/// documented default is <c>2.0</c>.
/// </param>
/// <param name="RefundGasFeeEstimateUsdt">
/// T72 MVP refund gas fee estimate in USDT (08 §3.4). Used by the amount
/// validation pipeline as input to <c>RefundDecisionService</c> when
/// classifying under/over/wrong-token cases. Validator stage 2 enforces
/// <c>value &gt; 0</c>; the seeded default is <c>2.0</c>. T74 energy
/// delegation will replace this estimate with a runtime-measured value.
/// </param>
public sealed record GasFeeSettings(
    decimal ProtectionRatio,
    decimal MinRefundThresholdRatio,
    decimal RefundGasFeeEstimateUsdt);
