using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Domain.Calculations;

namespace Skinora.Transactions.Application.GasFee;

/// <inheritdoc cref="IRefundDecisionService"/>
public sealed class RefundDecisionService : IRefundDecisionService
{
    private readonly IGasFeeSettingsProvider _settings;
    private readonly IBlockchainNetworkSettingsProvider _networkSettings;

    public RefundDecisionService(
        IGasFeeSettingsProvider settings,
        IBlockchainNetworkSettingsProvider networkSettings)
    {
        _settings = settings;
        _networkSettings = networkSettings;
    }

    public async Task<RefundDecision> ResolveBuyerRefundAsync(
        BlockchainNetwork network,
        decimal totalPaid,
        decimal gasFee,
        CancellationToken cancellationToken)
    {
        if (totalPaid < 0)
            throw new ArgumentOutOfRangeException(nameof(totalPaid), totalPaid, "Total paid must be non-negative.");
        if (gasFee < 0)
            throw new ArgumentOutOfRangeException(nameof(gasFee), gasFee, "Gas fee must be non-negative.");

        var ratio = await MinRefundThresholdRatioAsync(network, cancellationToken);
        return BuildDecision(totalPaid, gasFee, ratio);
    }

    public async Task<RefundDecision> ResolveOverpaymentRefundAsync(
        BlockchainNetwork network,
        decimal expected,
        decimal received,
        decimal gasFee,
        CancellationToken cancellationToken)
    {
        if (expected < 0)
            throw new ArgumentOutOfRangeException(nameof(expected), expected, "Expected amount must be non-negative.");
        if (received < 0)
            throw new ArgumentOutOfRangeException(nameof(received), received, "Received amount must be non-negative.");
        if (gasFee < 0)
            throw new ArgumentOutOfRangeException(nameof(gasFee), gasFee, "Gas fee must be non-negative.");

        var overpayment = FinancialCalculator.CalculateOverpayment(expected, received);
        var ratio = await MinRefundThresholdRatioAsync(network, cancellationToken);
        return BuildDecision(overpayment, gasFee, ratio);
    }

    public async Task<decimal> ResolveSellerPayoutAsync(
        BlockchainNetwork network,
        decimal price,
        decimal commissionAmount,
        decimal gasFee,
        CancellationToken cancellationToken)
    {
        // T149 (11 §F8 K8, 02 §4.7) — the payout rule is per network. TRON:
        // the existing gas-protection split, the seller bears the overage.
        // Ethereum: the platform absorbs the payout fee up to the per-transfer
        // cap and nothing comes off the seller; above the cap the payout waits
        // (08 §3.7.7 — T153/T158), it is never charged to the seller.
        if (!NetworkFeeRules.SellerBearsPayoutFeeOverage(network))
        {
            if (price < 0)
                throw new ArgumentOutOfRangeException(nameof(price), price, "Price must be non-negative.");
            if (gasFee < 0)
                throw new ArgumentOutOfRangeException(nameof(gasFee), gasFee, "Gas fee must be non-negative.");
            return price;
        }

        var settings = await _settings.GetAsync(cancellationToken);
        return FinancialCalculator.CalculateSellerPayout(price, commissionAmount, gasFee, settings.ProtectionRatio);
    }

    /// <summary>
    /// T149 — the minimum-refund multiplier is per network (02 §4.6, 06 §3.17):
    /// TRON's <c>min_refund_threshold_ratio</c>, Ethereum's
    /// <c>blockchain.ethereum.min_refund_threshold_ratio</c>.
    /// </summary>
    private async Task<decimal> MinRefundThresholdRatioAsync(
        BlockchainNetwork network, CancellationToken cancellationToken) =>
        (await _networkSettings.GetAsync(network, cancellationToken)).MinRefundThresholdRatio;

    private static RefundDecision BuildDecision(decimal totalPaid, decimal gasFee, decimal minRefundRatio)
    {
        var net = FinancialCalculator.CalculateRefund(totalPaid, gasFee);
        var threshold = FinancialCalculator.CalculateMinimumRefundThreshold(gasFee, minRefundRatio);

        if (net < 0m)
        {
            return new RefundDecision(
                Outcome: RefundOutcome.Block,
                NetRefund: net,
                Threshold: threshold,
                GasFee: gasFee,
                TotalPaid: totalPaid,
                Reason: RefundBlockedReason.NegativeAmount);
        }

        if (net < threshold)
        {
            return new RefundDecision(
                Outcome: RefundOutcome.Block,
                NetRefund: net,
                Threshold: threshold,
                GasFee: gasFee,
                TotalPaid: totalPaid,
                Reason: RefundBlockedReason.BelowMinimumThreshold);
        }

        return new RefundDecision(
            Outcome: RefundOutcome.Refund,
            NetRefund: net,
            Threshold: threshold,
            GasFee: gasFee,
            TotalPaid: totalPaid,
            Reason: null);
    }
}
