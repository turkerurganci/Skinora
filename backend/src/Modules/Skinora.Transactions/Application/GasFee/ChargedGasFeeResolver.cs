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

    public async Task<ResolvedGasFee> ResolveRefundFeeAsync(
        string? fromDepositAddress,
        string toAddress,
        decimal amount,
        StablecoinType token,
        CancellationToken cancellationToken)
    {
        // A refund is broadcast FROM its deposit address (the dispatch refuses
        // one without it), so that address is the only sender an estimate can
        // price. Asked without one, the sidecar would price a hot-wallet
        // transfer instead — and the hot wallet carries delegated Energy a
        // deposit does not, so the figure would come back low. No sender, no
        // estimate: the static setting is charged, as for any estimator outage.
        if (string.IsNullOrWhiteSpace(fromDepositAddress))
        {
            var settings = await _settings.GetAsync(cancellationToken);
            var fallback = settings.RefundGasFeeEstimateUsdt;
            _logger.LogWarning(
                "Gas fee estimate skipped for refund to {To}: no deposit address to price the transfer from — charging static fallback {Fallback} USDT.",
                toAddress, fallback);
            return new ResolvedGasFee(fallback, GasFeeSource.StaticFallback);
        }

        return await ResolveAsync(
            new GasFeeEstimateRequest(fromDepositAddress, toAddress, amount, token),
            static s => s.RefundGasFeeEstimateUsdt,
            "refund",
            cancellationToken);
    }

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
        var settings = await _settings.GetAsync(cancellationToken);
        var estimate = await _estimator.EstimateFeeUsdtAsync(request, cancellationToken);

        if (estimate is { } fee)
        {
            // Sanity ceiling. The estimate multiplies a chain probe by a live
            // exchange rate; a single bad quote, unit slip or misread decimal
            // turns into an unbounded deduction from a user's own money, and
            // nothing downstream would question it. The cap does NOT clamp —
            // clamping would silently charge a wrong-but-plausible amount.
            // Exceeding it means the estimate is not trustworthy, so the
            // static fallback is used and an operator can see why.
            var cap = settings.MaxChargedGasFeeUsdt;
            if (cap > 0m && fee > cap)
            {
                var capped = fallbackSelector(settings);
                _logger.LogError(
                    "Gas fee estimate {Fee} USDT for {Flow} to {To} exceeds the {Cap} USDT ceiling — refusing the estimate and charging the static fallback {Fallback} USDT. Investigate the price feed and chain probes.",
                    fee, flow, request.ToAddress, cap, capped);
                return new ResolvedGasFee(capped, GasFeeSource.EstimateRejected);
            }

            return new ResolvedGasFee(fee, GasFeeSource.RuntimeEstimate);
        }

        var fallback = fallbackSelector(settings);
        _logger.LogWarning(
            "Gas fee runtime estimate unavailable for {Flow} to {To} — charging static fallback {Fallback} USDT.",
            flow, request.ToAddress, fallback);
        return new ResolvedGasFee(fallback, GasFeeSource.StaticFallback);
    }
}
