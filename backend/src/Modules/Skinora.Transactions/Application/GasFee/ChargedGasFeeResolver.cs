using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Networks;

namespace Skinora.Transactions.Application.GasFee;

/// <inheritdoc cref="IChargedGasFeeResolver"/>
/// <remarks>
/// T148 — both halves are per network: the estimate comes from the network's
/// own service (<see cref="IBlockchainServiceRegistry.GasFee"/>) and the
/// fallback / ceiling from the network's own settings
/// (<see cref="IBlockchainNetworkSettingsProvider"/>), so a TRON fallback is
/// never charged for an Ethereum transfer or the other way round. A network
/// without a service answers "no estimate" and its fallback is charged.
/// </remarks>
public sealed class ChargedGasFeeResolver : IChargedGasFeeResolver
{
    private readonly IBlockchainServiceRegistry _services;
    private readonly IBlockchainNetworkSettingsProvider _settings;
    private readonly ILogger<ChargedGasFeeResolver> _logger;

    public ChargedGasFeeResolver(
        IBlockchainServiceRegistry services,
        IBlockchainNetworkSettingsProvider settings,
        ILogger<ChargedGasFeeResolver> logger)
    {
        _services = services;
        _settings = settings;
        _logger = logger;
    }

    public async Task<ResolvedGasFee> ResolveRefundFeeAsync(
        BlockchainNetwork network,
        string? fromDepositAddress,
        string toAddress,
        decimal amount,
        StablecoinType token,
        CancellationToken cancellationToken)
    {
        // A refund is broadcast FROM its deposit address (the dispatch skips a
        // refund row it cannot resolve a deposit for), so that address is the
        // only sender an estimate can price. Asked without one, the sidecar
        // would price a hot-wallet transfer instead — another account's Energy
        // and Bandwidth, so the figure could be wrong either way: low where the
        // deposit would burn, high where it would be delegated Energy. No
        // sender, no estimate: the static setting is charged, as for any
        // estimator outage.
        if (string.IsNullOrWhiteSpace(fromDepositAddress))
        {
            var settings = await _settings.GetAsync(network, cancellationToken);
            var fallback = settings.RefundGasFeeFallbackUsdt;
            _logger.LogWarning(
                "Gas fee estimate skipped for {Network} refund to {To}: no deposit address to price the transfer from — charging static fallback {Fallback} USDT.",
                network, toAddress, fallback);
            return new ResolvedGasFee(fallback, GasFeeSource.StaticFallback);
        }

        return await ResolveAsync(
            network,
            new GasFeeEstimateRequest(fromDepositAddress, toAddress, amount, token),
            static s => s.RefundGasFeeFallbackUsdt,
            "refund",
            cancellationToken);
    }

    public Task<ResolvedGasFee> ResolvePayoutFeeAsync(
        BlockchainNetwork network,
        string toAddress,
        decimal amount,
        StablecoinType token,
        CancellationToken cancellationToken) =>
        ResolveAsync(
            network,
            new GasFeeEstimateRequest(FromAddress: null, toAddress, amount, token),
            static s => s.PayoutGasFeeFallbackUsdt,
            "payout",
            cancellationToken);

    private async Task<ResolvedGasFee> ResolveAsync(
        BlockchainNetwork network,
        GasFeeEstimateRequest request,
        Func<BlockchainNetworkSettings, decimal> fallbackSelector,
        string flow,
        CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync(network, cancellationToken);
        var estimate = await _services.GasFee(network).EstimateFeeUsdtAsync(request, cancellationToken);

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
                    "Gas fee estimate {Fee} USDT for {Network} {Flow} to {To} exceeds the {Cap} USDT ceiling — refusing the estimate and charging the static fallback {Fallback} USDT. Investigate the price feed and chain probes.",
                    fee, network, flow, request.ToAddress, cap, capped);
                return new ResolvedGasFee(capped, GasFeeSource.EstimateRejected);
            }

            return new ResolvedGasFee(fee, GasFeeSource.RuntimeEstimate);
        }

        var fallback = fallbackSelector(settings);
        _logger.LogWarning(
            "Gas fee runtime estimate unavailable for {Network} {Flow} to {To} — charging static fallback {Fallback} USDT.",
            network, flow, request.ToAddress, fallback);
        return new ResolvedGasFee(fallback, GasFeeSource.StaticFallback);
    }
}
