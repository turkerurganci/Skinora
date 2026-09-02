using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;

namespace Skinora.Transactions.Tests.Unit.GasFee;

/// <summary>
/// Unit coverage for <see cref="ChargedGasFeeResolver"/>
/// (Prova-GasFeeChargedIsFixedGuess). The contract under test: a live
/// estimate is charged verbatim; an estimator outage falls back to the
/// flow-matching static setting — refund and payout deliberately fall back to
/// DIFFERENT constants, so the fallback selector itself carries load.
/// </summary>
public class ChargedGasFeeResolverTests
{
    private const decimal StaticRefundFee = 2m;
    private const decimal StaticPayoutFee = 0.50m;

    [Fact]
    public async Task RefundFee_UsesRuntimeEstimate_WhenAvailable()
    {
        var estimator = new StubEstimator { Result = 0.18m };
        var sut = BuildResolver(estimator);

        var resolved = await sut.ResolveRefundFeeAsync(
            "TDeposit", "TBuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(0.18m, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.RuntimeEstimate, resolved.Source);
        var request = Assert.Single(estimator.Requests);
        Assert.Equal("TDeposit", request.FromAddress);
        Assert.Equal("TBuyer", request.ToAddress);
        Assert.Equal(10.20m, request.Amount);
        Assert.Equal(StablecoinType.USDT, request.Token);
    }

    [Fact]
    public async Task RefundFee_FallsBackToStaticRefundSetting_WhenEstimateUnavailable()
    {
        var sut = BuildResolver(new StubEstimator { Result = null });

        var resolved = await sut.ResolveRefundFeeAsync(
            "TDeposit", "TBuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(StaticRefundFee, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.StaticFallback, resolved.Source);
    }

    [Fact]
    public async Task PayoutFee_UsesRuntimeEstimate_WithHotWalletSender()
    {
        var estimator = new StubEstimator { Result = 0.02m };
        var sut = BuildResolver(estimator);

        var resolved = await sut.ResolvePayoutFeeAsync(
            "TSeller", 100m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(0.02m, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.RuntimeEstimate, resolved.Source);
        var request = Assert.Single(estimator.Requests);
        // Payouts broadcast from the hot wallet — the sidecar resolves the
        // sender itself, so the request must not pin one.
        Assert.Null(request.FromAddress);
    }

    [Fact]
    public async Task PayoutFee_FallsBackToStaticPayoutSetting_NotTheRefundOne()
    {
        var sut = BuildResolver(new StubEstimator { Result = null });

        var resolved = await sut.ResolvePayoutFeeAsync(
            "TSeller", 100m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(StaticPayoutFee, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.StaticFallback, resolved.Source);
    }

    [Fact]
    public async Task ZeroEstimate_IsChargedAsZero_NotTreatedAsMissing()
    {
        // The measured Nile reality: delegated Energy covers the transfer and
        // the true cost is 0. Zero is a VALUE, not an absence — regressing it
        // to the fallback would re-introduce the 2.00 overcharge this round
        // removed.
        var sut = BuildResolver(new StubEstimator { Result = 0m });

        var resolved = await sut.ResolveRefundFeeAsync(
            "TDeposit", "TBuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(0m, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.RuntimeEstimate, resolved.Source);
    }

    private static ChargedGasFeeResolver BuildResolver(StubEstimator estimator) =>
        new(estimator, new StubSettings(), NullLogger<ChargedGasFeeResolver>.Instance);

    private sealed class StubEstimator : IGasFeeEstimator
    {
        public decimal? Result { get; set; }
        public List<GasFeeEstimateRequest> Requests { get; } = [];

        public Task<decimal?> EstimateFeeUsdtAsync(
            GasFeeEstimateRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Result);
        }
    }

    private sealed class StubSettings : IGasFeeSettingsProvider
    {
        public Task<GasFeeSettings> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new GasFeeSettings(
                ProtectionRatio: 0.10m,
                MinRefundThresholdRatio: 2m,
                RefundGasFeeEstimateUsdt: StaticRefundFee,
                PayoutGasFeeEstimateUsdt: StaticPayoutFee));
    }
}
