using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.GasFee;

/// <summary>Why a gas fee was charged without a live estimate (T160b).</summary>
public enum GasFeeFallbackReason
{
    /// <summary>The estimate could not be obtained (after the refund retries).</summary>
    Unavailable,

    /// <summary>An estimate came back above <c>max_charged_gas_fee_usdt</c> and was refused.</summary>
    Rejected,

    /// <summary>A refund without a deposit address — there was no sender to price.</summary>
    NoSender,
}

/// <summary>
/// T160b — told every time a gas fee is charged without a live estimate, so
/// the <c>gas-fee-fallback-charged</c> alarm sees it (the API host publishes
/// it as <c>skinora_backend_gas_fee_fallback_charged_total</c>).
/// </summary>
public interface IGasFeeFallbackObserver
{
    /// <param name="network">The transfer's network.</param>
    /// <param name="flow">"refund", "hot-wallet refund" or "payout".</param>
    /// <param name="reason">Why no live estimate was charged.</param>
    /// <param name="measured">
    /// <c>true</c> when the last measured estimate was charged
    /// (<see cref="MeasuredGasFeeCache"/>), <c>false</c> for the static setting.
    /// </param>
    void FallbackCharged(BlockchainNetwork network, string flow, GasFeeFallbackReason reason, bool measured);
}

/// <summary>Observer that records nothing (tests, hosts without metrics).</summary>
public sealed class NullGasFeeFallbackObserver : IGasFeeFallbackObserver
{
    public static readonly NullGasFeeFallbackObserver Instance = new();

    public void FallbackCharged(BlockchainNetwork network, string flow, GasFeeFallbackReason reason, bool measured)
    {
    }
}
