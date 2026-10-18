using Skinora.Transactions.Tests.Helpers;
using Skinora.Transactions.Application.Networks;
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
    private const decimal MaxChargedFee = 10m;

    [Fact]
    public async Task RefundFee_UsesRuntimeEstimate_WhenAvailable()
    {
        var estimator = new StubEstimator { Result = 0.18m };
        var sut = BuildResolver(estimator);

        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.TRON, "TDeposit", "TBuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

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
            BlockchainNetwork.TRON, "TDeposit", "TBuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(StaticRefundFee, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.StaticFallback, resolved.Source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefundFee_WithoutDepositAddress_ChargesStaticFallback_WithoutEstimating(
        string? depositAddress)
    {
        // A refund is broadcast FROM its deposit, so the deposit is the only
        // sender an estimate can price. Sent without one, the sidecar would
        // price a hot-wallet transfer instead — another account's Energy and
        // Bandwidth, so the figure could be wrong in either direction.
        // The estimator's answer here (0.18) differs from the fallback (2.00)
        // on purpose: a regression that still asks it must show.
        var estimator = new StubEstimator { Result = 0.18m };
        var sut = BuildResolver(estimator);

        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.TRON, depositAddress, "TBuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(StaticRefundFee, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.StaticFallback, resolved.Source);
        Assert.Empty(estimator.Requests);
    }

    [Fact]
    public async Task PayoutFee_UsesRuntimeEstimate_WithHotWalletSender()
    {
        var estimator = new StubEstimator { Result = 0.02m };
        var sut = BuildResolver(estimator);

        var resolved = await sut.ResolvePayoutFeeAsync(
            BlockchainNetwork.TRON, "TSeller", 100m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(0.02m, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.RuntimeEstimate, resolved.Source);
        var request = Assert.Single(estimator.Requests);
        // Payouts broadcast from the hot wallet — the sidecar resolves the
        // sender itself, so the request must not pin one. How "no sender"
        // reaches the wire is pinned by BlockchainSidecarEstimateFeeContractTests.
        Assert.Null(request.FromAddress);
    }

    [Fact]
    public async Task PayoutFee_FallsBackToStaticPayoutSetting_NotTheRefundOne()
    {
        var sut = BuildResolver(new StubEstimator { Result = null });

        var resolved = await sut.ResolvePayoutFeeAsync(
            BlockchainNetwork.TRON, "TSeller", 100m, StablecoinType.USDT, CancellationToken.None);

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
            BlockchainNetwork.TRON, "TDeposit", "TBuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(0m, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.RuntimeEstimate, resolved.Source);
    }

    [Fact]
    public async Task EstimateAboveTheCeiling_IsRefused_NotClamped()
    {
        // The ceiling exists because the estimate multiplies a chain probe by a
        // LIVE exchange rate: one bad quote or unit slip becomes an unbounded
        // deduction from the user's own money, and nothing downstream questions
        // it. Refusing rather than clamping is the point — a clamped figure
        // would be wrong but plausible, and would ship silently.
        var sut = BuildResolver(new StubEstimator { Result = 250m }, cap: 10m);

        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.TRON, "TDeposit", "TBuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(StaticRefundFee, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.EstimateRejected, resolved.Source);
    }

    [Fact]
    public async Task EstimateExactlyAtTheCeiling_IsAccepted()
    {
        var sut = BuildResolver(new StubEstimator { Result = 10m }, cap: 10m);

        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.TRON, "TDeposit", "TBuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(10m, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.RuntimeEstimate, resolved.Source);
    }

    [Fact]
    public async Task PayoutEstimateAboveTheCeiling_IsAlsoRefused()
    {
        var sut = BuildResolver(new StubEstimator { Result = 99m }, cap: 10m);

        var resolved = await sut.ResolvePayoutFeeAsync(
            BlockchainNetwork.TRON, "TSeller", 100m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(StaticPayoutFee, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.EstimateRejected, resolved.Source);
    }

    [Fact]
    public async Task ZeroCeiling_DisablesTheCheck()
    {
        // Documented escape hatch: 0 means "no ceiling". Without this an
        // operator who cleared the setting would silently get a zero ceiling
        // that refuses every estimate.
        var sut = BuildResolver(new StubEstimator { Result = 250m }, cap: 0m);

        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.TRON, "TDeposit", "TBuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(250m, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.RuntimeEstimate, resolved.Source);
    }

    private static ChargedGasFeeResolver BuildResolver(
        StubEstimator estimator, decimal? cap = null) =>
        new(FakeBlockchainServiceRegistry.ForTron(gasFee: estimator),
            new StubSettings { Cap = cap ?? MaxChargedFee },
            NullLogger<ChargedGasFeeResolver>.Instance);

    // -------------------- T148 — per-network estimate and fallback --------------------

    [Fact]
    public async Task Ethereum_Is_Priced_By_Ethereums_Service_Not_Trons()
    {
        var tron = new StubEstimator { Result = 2.40m };
        var ethereum = new StubEstimator { Result = 0.07m };
        var registry = FakeBlockchainServiceRegistry.ForTron(gasFee: tron)
            .With(BlockchainNetwork.ETHEREUM, gasFee: ethereum);
        var sut = new ChargedGasFeeResolver(
            registry, new StubSettings(), NullLogger<ChargedGasFeeResolver>.Instance);

        var resolved = await sut.ResolvePayoutFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xseller", 20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(0.07m, resolved.FeeUsdt);
        Assert.Single(ethereum.Requests);
        Assert.Empty(tron.Requests);
    }

    [Fact]
    public async Task Unconfigured_Ethereum_Charges_Ethereums_Fallback_Never_Trons()
    {
        // Until T150 Ethereum has no service: the registry hands out the
        // fail-closed client (no estimate) and the fallback must be Ethereum's
        // own row, not TRON's 2 USDT.
        var tron = new StubEstimator { Result = 2.40m };
        var sut = new ChargedGasFeeResolver(
            FakeBlockchainServiceRegistry.ForTron(gasFee: tron),
            new StubSettings(),
            NullLogger<ChargedGasFeeResolver>.Instance);

        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(EthereumRefundFallback, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.StaticFallback, resolved.Source);
        Assert.Empty(tron.Requests);
    }

    private const decimal EthereumRefundFallback = 0.50m;

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

    private sealed class StubSettings : IBlockchainNetworkSettingsProvider
    {
        public decimal Cap { get; set; } = MaxChargedFee;

        public Task<BlockchainNetworkSettings> GetAsync(
            BlockchainNetwork network, CancellationToken cancellationToken) =>
            Task.FromResult(network == BlockchainNetwork.TRON
                ? BlockchainNetworkSettingsProvider.TronDefaults with
                {
                    RefundGasFeeFallbackUsdt = StaticRefundFee,
                    PayoutGasFeeFallbackUsdt = StaticPayoutFee,
                    MaxChargedGasFeeUsdt = Cap,
                }
                : BlockchainNetworkSettingsProvider.EthereumDefaults with
                {
                    RefundGasFeeFallbackUsdt = EthereumRefundFallback,
                    MaxChargedGasFeeUsdt = Cap,
                });
    }
}
