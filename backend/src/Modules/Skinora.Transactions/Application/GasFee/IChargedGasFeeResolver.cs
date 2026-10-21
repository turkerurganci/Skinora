using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.GasFee;

/// <summary>
/// The single answer to "how much gas do we charge the user for this outbound
/// transfer?" — runtime estimate first (<see cref="IGasFeeEstimator"/>),
/// static <c>blockchain.*_gas_fee_estimate_usdt</c> setting as fallback (T160b:
/// a refund's estimate is retried on a retryable failure and its fallback is
/// the last measured estimate of the same kind while fresh), so
/// consumers (<c>PaymentRefundToBuyerConsumer</c>, <c>AmountValidationService</c>,
/// <c>SellerPayoutQueueJob</c>) never carry the fallback logic themselves and
/// an estimator outage degrades to the pre-round behaviour instead of
/// blocking refunds or payouts.
/// </summary>
public interface IChargedGasFeeResolver
{
    /// <summary>
    /// Fee charged on a refund-family transfer (deposit → recipient). Without a
    /// <paramref name="fromDepositAddress"/> there is no sender to price, so no
    /// estimate is asked for and the refund fallback is charged (T160b: the last
    /// measured deposit refund while fresh, else the static refund setting).
    /// <paramref name="network"/> is the transfer's network (T148): its service
    /// prices it and its fallback / ceiling apply.
    /// </summary>
    Task<ResolvedGasFee> ResolveRefundFeeAsync(
        BlockchainNetwork network,
        string? fromDepositAddress,
        string toAddress,
        decimal amount,
        StablecoinType token,
        CancellationToken cancellationToken);

    /// <summary>
    /// T158 (f) — fee charged on a refund that leaves the network's HOT wallet
    /// because the deposit was swept, or is being swept (08 §3.7.6
    /// "Kaynaklar"; Ethereum — <c>RefundSourceResolver</c>). No sender is
    /// passed: the estimate is asked with <c>fromAddress</c> absent, which the
    /// service prices as a hot-wallet transfer (no fuel — T154 (iii)); the
    /// static refund setting is the fallback and the ceiling applies as for
    /// any refund.
    /// </summary>
    Task<ResolvedGasFee> ResolveHotWalletRefundFeeAsync(
        BlockchainNetwork network,
        string toAddress,
        decimal amount,
        StablecoinType token,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fee used in the seller-payout gas-protection split (hot wallet → seller)
    /// on <paramref name="network"/> (T148). Priced from the hot wallet; the
    /// static payout setting is the fallback.
    /// </summary>
    Task<ResolvedGasFee> ResolvePayoutFeeAsync(
        BlockchainNetwork network,
        string toAddress,
        decimal amount,
        StablecoinType token,
        CancellationToken cancellationToken);
}

public enum GasFeeSource
{
    /// <summary>Live sidecar estimate — the exact pre-send cost.</summary>
    RuntimeEstimate,

    /// <summary>Static SystemSetting — estimator unavailable.</summary>
    StaticFallback,

    /// <summary>
    /// T160b — refund only: the estimator stayed silent after its retries and
    /// the last measured estimate of the same network, sender and token (at
    /// most <see cref="MeasuredGasFeeCache.Ttl"/> old) was charged instead of
    /// the static setting.
    /// </summary>
    MeasuredFallback,

    /// <summary>
    /// An estimate came back but exceeded <c>blockchain.max_charged_gas_fee_usdt</c>,
    /// so it was refused and the fallback used instead (T160b: for a refund
    /// the last measured estimate while fresh, else the static setting). Distinct from
    /// <see cref="StaticFallback"/> on purpose: that one means the estimator
    /// was silent, this one means it spoke and was not believed — which is an
    /// operator signal, not routine degradation.
    /// </summary>
    EstimateRejected,
}

public sealed record ResolvedGasFee(decimal FeeUsdt, GasFeeSource Source);
