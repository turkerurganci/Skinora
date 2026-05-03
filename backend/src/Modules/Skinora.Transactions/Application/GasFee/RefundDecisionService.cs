using Skinora.Shared.Events;
using Skinora.Transactions.Domain.Calculations;

namespace Skinora.Transactions.Application.GasFee;

/// <inheritdoc cref="IRefundDecisionService"/>
public sealed class RefundDecisionService : IRefundDecisionService
{
    private readonly IGasFeeSettingsProvider _settings;

    public RefundDecisionService(IGasFeeSettingsProvider settings)
    {
        _settings = settings;
    }

    public async Task<RefundDecision> ResolveBuyerRefundAsync(
        decimal totalPaid,
        decimal gasFee,
        CancellationToken cancellationToken)
    {
        if (totalPaid < 0)
            throw new ArgumentOutOfRangeException(nameof(totalPaid), totalPaid, "Total paid must be non-negative.");
        if (gasFee < 0)
            throw new ArgumentOutOfRangeException(nameof(gasFee), gasFee, "Gas fee must be non-negative.");

        var settings = await _settings.GetAsync(cancellationToken);
        return BuildDecision(totalPaid, gasFee, settings.MinRefundThresholdRatio);
    }

    public async Task<RefundDecision> ResolveOverpaymentRefundAsync(
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
        var settings = await _settings.GetAsync(cancellationToken);
        return BuildDecision(overpayment, gasFee, settings.MinRefundThresholdRatio);
    }

    public async Task<decimal> ResolveSellerPayoutAsync(
        decimal price,
        decimal commissionAmount,
        decimal gasFee,
        CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync(cancellationToken);
        return FinancialCalculator.CalculateSellerPayout(price, commissionAmount, gasFee, settings.ProtectionRatio);
    }

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
