using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.GasFee;

/// <inheritdoc cref="IChargedGasFeeResolver"/>
public sealed class ChargedGasFeeResolver : IChargedGasFeeResolver
{
    private readonly IGasFeeEstimator _estimator;
    private readonly IGasFeeSettingsProvider _settings;
    private readonly ILogger<ChargedGasFeeResolver> _logger;

    public ChargedGasFeeResolver(
        IGasFeeEstimator estimator,
        IGasFeeSettingsProvider settings,
        ILogger<ChargedGasFeeResolver> logger)
    {
        _estimator = estimator;
        _settings = settings;
        _logger = logger;
    }

    public Task<ResolvedGasFee> ResolveRefundFeeAsync(
        string? fromDepositAddress,
        string toAddress,
        decimal amount,
        StablecoinType token,
        CancellationToken cancellationToken) =>
        ResolveAsync(
            new GasFeeEstimateRequest(fromDepositAddress, toAddress, amount, token),
            static s => s.RefundGasFeeEstimateUsdt,
            "refund",
            cancellationToken);

    public Task<ResolvedGasFee> ResolvePayoutFeeAsync(
        string toAddress,
        decimal amount,
        StablecoinType token,
        CancellationToken cancellationToken) =>
        ResolveAsync(
            new GasFeeEstimateRequest(FromAddress: null, toAddress, amount, token),
            static s => s.PayoutGasFeeEstimateUsdt,
            "payout",
            cancellationToken);

    private async Task<ResolvedGasFee> ResolveAsync(
        GasFeeEstimateRequest request,
        Func<GasFeeSettings, decimal> fallbackSelector,
        string flow,
        CancellationToken cancellationToken)
    {
        var estimate = await _estimator.EstimateFeeUsdtAsync(request, cancellationToken);
        if (estimate is { } fee)
        {
            return new ResolvedGasFee(fee, GasFeeSource.RuntimeEstimate);
        }

        var settings = await _settings.GetAsync(cancellationToken);
        var fallback = fallbackSelector(settings);
        _logger.LogWarning(
            "Gas fee runtime estimate unavailable for {Flow} to {To} — charging static fallback {Fallback} USDT.",
            flow, request.ToAddress, fallback);
        return new ResolvedGasFee(fallback, GasFeeSource.StaticFallback);
    }
}
