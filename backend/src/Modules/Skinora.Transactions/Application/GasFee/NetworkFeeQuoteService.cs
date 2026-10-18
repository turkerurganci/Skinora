using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Domain.Calculations;

namespace Skinora.Transactions.Application.GasFee;

/// <inheritdoc cref="INetworkFeeQuoteService"/>
public sealed class NetworkFeeQuoteService : INetworkFeeQuoteService
{
    private readonly IBlockchainServiceRegistry _services;
    private readonly IBlockchainNetworkSettingsProvider _settings;
    private readonly SweepCostEstimateCache _cache;
    private readonly ILogger<NetworkFeeQuoteService> _logger;

    public NetworkFeeQuoteService(
        IBlockchainServiceRegistry services,
        IBlockchainNetworkSettingsProvider settings,
        SweepCostEstimateCache cache,
        ILogger<NetworkFeeQuoteService> logger)
    {
        _services = services;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public async Task<NetworkFeeQuote> GetBuyerNetworkFeeAsync(
        BlockchainNetwork network, StablecoinType token, CancellationToken cancellationToken)
    {
        if (!NetworkFeeRules.BuyerPaysNetworkFeeLine(network))
            return new NetworkFeeQuote(network, 0m, NetworkFeeSource.NotCharged);

        var settings = await _settings.GetAsync(network, cancellationToken);
        // A network whose buyer pays a line has both settings seeded
        // (BlockchainNetworkSettingKeys); a missing one is a wiring error.
        var buffer = settings.NetworkFeeBufferRatio
            ?? throw new InvalidOperationException($"{network} charges a network fee line but has no buffer setting.");
        var fallback = FinancialCalculator.RoundUpToCents(settings.NetworkFeeFallbackUsdt
            ?? throw new InvalidOperationException($"{network} charges a network fee line but has no fallback setting."));

        if (!_cache.TryGet(network, token, out var estimate))
        {
            estimate = await _services.GasFee(network).EstimateSweepCostAsync(token, cancellationToken);
            _cache.Set(network, token, estimate);
        }

        if (estimate is null)
        {
            _logger.LogWarning(
                "{Network} network fee line: sweep estimate unavailable — using the static fallback {Fallback} USDT.",
                network, fallback);
            return new NetworkFeeQuote(network, fallback, NetworkFeeSource.StaticFallback);
        }

        var line = FinancialCalculator.CalculateTronNetworkFeeLine(
            estimate.SweepCostSun, estimate.NativePriceUsdt, buffer);

        // Same sanity ceiling as the refund / payout deductions: the line
        // multiplies a chain probe by a live exchange rate, and a bad quote
        // would otherwise become an unbounded charge on the buyer. Refused,
        // not clamped — a clamped figure is wrong but plausible.
        var cap = settings.MaxChargedGasFeeUsdt;
        if (cap > 0m && line > cap)
        {
            _logger.LogError(
                "{Network} network fee line {Line} USDT (sweep {SweepCostSun} sun at {Price}) exceeds the {Cap} USDT ceiling — refusing the estimate and using the static fallback {Fallback} USDT. Investigate the price feed and chain probes.",
                network, line, estimate.SweepCostSun, estimate.NativePriceUsdt, cap, fallback);
            return new NetworkFeeQuote(network, fallback, NetworkFeeSource.EstimateRejected);
        }

        return new NetworkFeeQuote(network, line, NetworkFeeSource.RuntimeEstimate);
    }
}
