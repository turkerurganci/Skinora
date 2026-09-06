using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.GasFee;

/// <summary>
/// Pre-send gas cost of one outbound TRC-20 transfer, in USDT — the runtime
/// replacement for the static <c>blockchain.*_gas_fee_estimate_usdt</c>
/// constants (Prova-GasFeeChargedIsFixedGuess, owner decision 2026-09-02:
/// charge the computed cost, not a guess). Backed by the blockchain sidecar's
/// <c>POST /api/transfer/estimate-fee</c>, which simulates the exact transfer
/// (`triggerconstantcontract`), nets out the hot wallet's own Energy and the
/// sender's Bandwidth, prices the shortfall at the chain's current unit fees
/// and converts burned TRX to USDT at the live exchange rate.
/// </summary>
public interface IGasFeeEstimator
{
    /// <summary>
    /// Estimated fee in USDT, or <c>null</c> when no estimate could be
    /// obtained (sidecar down, chain probe failed, price feed unavailable).
    /// Callers MUST treat <c>null</c> as "fall back to the static setting" —
    /// an estimate outage may never block a money path.
    /// </summary>
    Task<decimal?> EstimateFeeUsdtAsync(
        GasFeeEstimateRequest request, CancellationToken cancellationToken);
}

/// <param name="FromAddress">
/// Sender of the eventual transfer. <c>null</c> → the hot wallet (payout
/// path); refunds pass the deposit address so the sidecar reads the actual
/// sender's Bandwidth and simulates from the account that holds the tokens.
/// </param>
/// <param name="ToAddress">Destination of the eventual transfer.</param>
/// <param name="Amount">Human-unit amount (gross is fine — Energy cost is
/// amount-insensitive; the value only needs to be coverable by the sender's
/// balance for the simulation to take the success path).</param>
/// <param name="Token">Stablecoin being transferred.</param>
public sealed record GasFeeEstimateRequest(
    string? FromAddress,
    string ToAddress,
    decimal Amount,
    StablecoinType Token);
