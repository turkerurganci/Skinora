using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.GasFee;

/// <summary>
/// The single answer to "how much gas do we charge the user for this outbound
/// transfer?" — runtime estimate first (<see cref="IGasFeeEstimator"/>),
/// static <c>blockchain.*_gas_fee_estimate_usdt</c> setting as fallback, so
/// consumers (<c>PaymentRefundToBuyerConsumer</c>, <c>AmountValidationService</c>,
/// <c>SellerPayoutQueueJob</c>) never carry the fallback logic themselves and
/// an estimator outage degrades to the pre-round behaviour instead of
/// blocking refunds or payouts.
/// </summary>
public interface IChargedGasFeeResolver
{
    /// <summary>Fee charged on a refund-family transfer (deposit → recipient).</summary>
    Task<ResolvedGasFee> ResolveRefundFeeAsync(
        string? fromDepositAddress,
        string toAddress,
        decimal amount,
        StablecoinType token,
        CancellationToken cancellationToken);

    /// <summary>Fee used in the seller-payout gas-protection split (hot wallet → seller).</summary>
    Task<ResolvedGasFee> ResolvePayoutFeeAsync(
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
    /// An estimate came back but exceeded <c>blockchain.max_charged_gas_fee_usdt</c>,
    /// so it was refused and the static setting used instead. Distinct from
    /// <see cref="StaticFallback"/> on purpose: that one means the estimator
    /// was silent, this one means it spoke and was not believed — which is an
    /// operator signal, not routine degradation.
    /// </summary>
    EstimateRejected,
}

public sealed record ResolvedGasFee(decimal FeeUsdt, GasFeeSource Source);
