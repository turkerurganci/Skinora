using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Networks;

namespace Skinora.Transactions.Application.GasFee;

/// <inheritdoc cref="IChargedGasFeeResolver"/>
/// <remarks>
/// <para>
/// T148 — both halves are per network: the estimate comes from the network's
/// own service (<see cref="IBlockchainServiceRegistry.GasFee"/>) and the
/// fallback / ceiling from the network's own settings
/// (<see cref="IBlockchainNetworkSettingsProvider"/>), so a TRON fallback is
/// never charged for an Ethereum transfer or the other way round. A network
/// without a service answers "no estimate" and its fallback is charged.
/// </para>
/// <para>
/// T160b (öneriyle kaydedildi — ⚠ PARA) — a refund's estimate is asked
/// again after a retryable failure (<see cref="GasFeeEstimateRetryPolicy"/>:
/// 3 calls within 12 s), because the deduction is fixed once and a node's
/// passing lag (T159b B1, <c>block not found</c>) used to become a permanent
/// fallback charge. When there is still no estimate the refund charges the
/// last measured estimate of the same kind (<see cref="MeasuredGasFeeCache"/>,
/// at most 6 h old), else the static setting; every fallback is reported to
/// <see cref="IGasFeeFallbackObserver"/> (alarm <c>gas-fee-fallback-charged</c>).
/// The payout keeps one call and the static setting — its split is a seller
/// rule T160b does not touch — but is reported to the alarm too.
/// </para>
/// </remarks>
public sealed class ChargedGasFeeResolver : IChargedGasFeeResolver
{
    /// <summary>Flow names (logs, the fallback metric's <c>flow</c> label).</summary>
    public const string RefundFlow = "refund";
    public const string HotWalletRefundFlow = "hot-wallet refund";
    public const string PayoutFlow = "payout";

    private static readonly Flow Refund =
        new(RefundFlow, GasFeeSender.Deposit, static s => s.RefundGasFeeFallbackUsdt, IsRefund: true);
    private static readonly Flow HotWalletRefund =
        new(HotWalletRefundFlow, GasFeeSender.HotWallet, static s => s.RefundGasFeeFallbackUsdt, IsRefund: true);
    private static readonly Flow Payout =
        new(PayoutFlow, GasFeeSender.HotWallet, static s => s.PayoutGasFeeFallbackUsdt, IsRefund: false);

    private readonly IBlockchainServiceRegistry _services;
    private readonly IBlockchainNetworkSettingsProvider _settings;
    private readonly MeasuredGasFeeCache _measured;
    private readonly IGasFeeFallbackObserver _fallbackObserver;
    private readonly GasFeeEstimateRetryPolicy _refundRetry;
    private readonly TimeProvider _clock;
    private readonly ILogger<ChargedGasFeeResolver> _logger;

    public ChargedGasFeeResolver(
        IBlockchainServiceRegistry services,
        IBlockchainNetworkSettingsProvider settings,
        MeasuredGasFeeCache measured,
        IGasFeeFallbackObserver fallbackObserver,
        GasFeeEstimateRetryPolicy refundRetry,
        TimeProvider clock,
        ILogger<ChargedGasFeeResolver> logger)
    {
        _services = services;
        _settings = settings;
        _measured = measured;
        _fallbackObserver = fallbackObserver;
        _refundRetry = refundRetry;
        _clock = clock;
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
        // sender, no estimate: the fallback is charged, as for any estimator
        // outage (T160b: the last measured deposit refund while it is fresh).
        if (string.IsNullOrWhiteSpace(fromDepositAddress))
        {
            var settings = await _settings.GetAsync(network, cancellationToken);
            _logger.LogWarning(
                "Gas fee estimate skipped for {Network} refund to {To}: no deposit address to price the transfer from.",
                network, toAddress);
            return Fallback(network, settings, Refund, token, toAddress, GasFeeFallbackReason.NoSender);
        }

        return await ResolveAsync(
            network,
            new GasFeeEstimateRequest(fromDepositAddress, toAddress, amount, token),
            Refund,
            cancellationToken);
    }

    public Task<ResolvedGasFee> ResolveHotWalletRefundFeeAsync(
        BlockchainNetwork network,
        string toAddress,
        decimal amount,
        StablecoinType token,
        CancellationToken cancellationToken) =>
        // No sender in the request = the service prices its hot wallet (05 §3.3a
        // estimate-fee: "fromAddress absent = hot wallet").
        ResolveAsync(
            network,
            new GasFeeEstimateRequest(FromAddress: null, toAddress, amount, token),
            HotWalletRefund,
            cancellationToken);

    public Task<ResolvedGasFee> ResolvePayoutFeeAsync(
        BlockchainNetwork network,
        string toAddress,
        decimal amount,
        StablecoinType token,
        CancellationToken cancellationToken) =>
        ResolveAsync(
            network,
            new GasFeeEstimateRequest(FromAddress: null, toAddress, amount, token),
            Payout,
            cancellationToken);

    private async Task<ResolvedGasFee> ResolveAsync(
        BlockchainNetwork network,
        GasFeeEstimateRequest request,
        Flow flow,
        CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync(network, cancellationToken);
        var estimate = await EstimateAsync(
            network,
            request,
            flow,
            flow.IsRefund ? _refundRetry : GasFeeEstimateRetryPolicy.SingleAttempt,
            cancellationToken);

        if (estimate is { } fee)
        {
            // Sanity ceiling. The estimate multiplies a chain probe by a live
            // exchange rate; a single bad quote, unit slip or misread decimal
            // turns into an unbounded deduction from a user's own money, and
            // nothing downstream would question it. The cap does NOT clamp —
            // clamping would silently charge a wrong-but-plausible amount.
            // Exceeding it means the estimate is not trustworthy, so the
            // fallback is used and an operator can see why.
            var cap = settings.MaxChargedGasFeeUsdt;
            if (cap > 0m && fee > cap)
            {
                _logger.LogError(
                    "Gas fee estimate {Fee} USDT for {Network} {Flow} to {To} exceeds the {Cap} USDT ceiling — refusing the estimate. Investigate the price feed and chain probes.",
                    fee, network, flow.Name, request.ToAddress, cap);
                return Fallback(network, settings, flow, request.Token, request.ToAddress, GasFeeFallbackReason.Rejected);
            }

            // T160b — a charged estimate is the measurement the next fallback
            // of the same kind uses; a refused one never is.
            _measured.Record(network, flow.Sender, request.Token, fee);
            return new ResolvedGasFee(fee, GasFeeSource.RuntimeEstimate);
        }

        return Fallback(network, settings, flow, request.Token, request.ToAddress, GasFeeFallbackReason.Unavailable);
    }

    /// <summary>
    /// T160b — the estimate, asked again after a retryable failure while the
    /// policy's attempts and budget allow (<see cref="GasFeeEstimateRetryPolicy"/>
    /// says why the budget, not only the count, bounds it). A retry the budget
    /// cuts short is a failure like any other: the fallback follows. The
    /// caller's own cancellation still propagates.
    /// </summary>
    private async Task<decimal?> EstimateAsync(
        BlockchainNetwork network,
        GasFeeEstimateRequest request,
        Flow flow,
        GasFeeEstimateRetryPolicy policy,
        CancellationToken cancellationToken)
    {
        var estimator = _services.GasFee(network);
        var started = _clock.GetTimestamp();
        var result = await estimator.EstimateFeeAsync(request, cancellationToken);

        for (var attempt = 2;
             result.FeeUsdt is null && result.Retryable && attempt <= policy.MaxAttempts;
             attempt++)
        {
            var delay = policy.DelayBefore(attempt);
            if (_clock.GetElapsedTime(started) + delay >= policy.Budget)
            {
                break;
            }

            _logger.LogInformation(
                "Gas fee estimate for {Network} {Flow} to {To} failed ({Failure}) — asking again in {Delay} (attempt {Attempt}/{MaxAttempts}).",
                network, flow.Name, request.ToAddress, result.Failure, delay, attempt, policy.MaxAttempts);
            await Task.Delay(delay, _clock, cancellationToken);

            var remaining = policy.Budget - _clock.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            using var deadline = new CancellationTokenSource(remaining, _clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            try
            {
                result = await estimator.EstimateFeeAsync(request, linked.Token);
            }
            catch (OperationCanceledException) when (
                deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                result = GasFeeEstimateAttempt.Failed(retryable: false, "retry budget used up");
            }

            if (result.FeeUsdt is not null)
            {
                _logger.LogInformation(
                    "Gas fee estimate for {Network} {Flow} to {To} obtained on attempt {Attempt}.",
                    network, flow.Name, request.ToAddress, attempt);
            }
        }

        return result.FeeUsdt;
    }

    /// <summary>
    /// What is charged without a live estimate: for a refund the last
    /// measured estimate of the same network, sender and token while it is
    /// fresh (<see cref="MeasuredGasFeeCache"/>), otherwise the flow's static
    /// setting. Every fallback is reported to the alarm.
    /// </summary>
    private ResolvedGasFee Fallback(
        BlockchainNetwork network,
        BlockchainNetworkSettings settings,
        Flow flow,
        StablecoinType token,
        string toAddress,
        GasFeeFallbackReason reason)
    {
        var rejected = reason == GasFeeFallbackReason.Rejected;
        if (flow.IsRefund && _measured.TryGetFresh(network, flow.Sender, token, out var measurement))
        {
            _logger.LogWarning(
                "Gas fee for {Network} {Flow} to {To} charged without a live estimate ({Reason}) — charging the last measured estimate {Fee} USDT ({Sender}, {Token}, measured {MeasuredAt:O}).",
                network, flow.Name, toAddress, reason, measurement.FeeUsdt, flow.Sender, token, measurement.MeasuredAt);
            _fallbackObserver.FallbackCharged(network, flow.Name, reason, measured: true);
            return new ResolvedGasFee(
                measurement.FeeUsdt,
                rejected ? GasFeeSource.EstimateRejected : GasFeeSource.MeasuredFallback);
        }

        var fallback = flow.StaticFallback(settings);
        _logger.LogWarning(
            "Gas fee for {Network} {Flow} to {To} charged without a live estimate ({Reason}) — charging static fallback {Fallback} USDT.",
            network, flow.Name, toAddress, reason, fallback);
        _fallbackObserver.FallbackCharged(network, flow.Name, reason, measured: false);
        return new ResolvedGasFee(
            fallback,
            rejected ? GasFeeSource.EstimateRejected : GasFeeSource.StaticFallback);
    }

    /// <param name="Name">For logs and the metric label.</param>
    /// <param name="Sender">Whose transfer is priced — the measurement key.</param>
    /// <param name="StaticFallback">The setting charged when nothing better is known.</param>
    /// <param name="IsRefund">Refund family: retried, and falls back to the last measurement.</param>
    private sealed record Flow(
        string Name,
        GasFeeSender Sender,
        Func<BlockchainNetworkSettings, decimal> StaticFallback,
        bool IsRefund);
}
