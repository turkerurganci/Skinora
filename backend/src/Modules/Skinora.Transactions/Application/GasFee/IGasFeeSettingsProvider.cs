namespace Skinora.Transactions.Application.GasFee;

/// <summary>
/// Reads <c>gas_fee_protection_ratio</c> and <c>min_refund_threshold_ratio</c>
/// from SystemSettings (02 §4.7, 09 §14.4). Mirrors
/// <c>IReputationThresholdsProvider</c> (T43) — caller treats every call as
/// cheap, no caching contract is exposed.
/// </summary>
public interface IGasFeeSettingsProvider
{
    Task<GasFeeSettings> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Snapshot of the two gas-fee-related settings that drive
/// <c>RefundDecisionService</c>.
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
public sealed record GasFeeSettings(decimal ProtectionRatio, decimal MinRefundThresholdRatio);
