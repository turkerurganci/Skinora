using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Tests.Helpers;

namespace Skinora.Transactions.Tests.Unit.GasFee;

/// <summary>
/// Unit coverage for <see cref="RefundDecisionService"/> — exercises the
/// four T53 acceptance criteria (gas-fee-protection threshold, seller payout
/// deduction, refund minus gas fee, minimum refund threshold) via a stub
/// <see cref="IGasFeeSettingsProvider"/>. Pure decision logic; no DB.
/// </summary>
public class RefundDecisionServiceTests
{
    /// <summary>
    /// Stub implementation that returns whatever pair the test arranges.
    /// </summary>
    private sealed class StubSettingsProvider : IGasFeeSettingsProvider
    {
        public GasFeeSettings Settings { get; init; } =
            new(ProtectionRatio: 0.10m, MinRefundThresholdRatio: 2m, RefundGasFeeEstimateUsdt: 2m, PayoutGasFeeEstimateUsdt: 0.50m, MaxChargedGasFeeUsdt: 10m);

        public int CallCount { get; private set; }

        public Task<GasFeeSettings> GetAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Settings);
        }
    }

    private static RefundDecisionService NewService(GasFeeSettings? settings = null)
    {
        var stub = new StubSettingsProvider
        {
            Settings = settings ?? new GasFeeSettings(0.10m, 2m, 2m, 0.50m, 10m),
        };
        return GasFeeSettingsBackedNetworkSettings.NewRefundDecisionService(stub);
    }

    // ─── Buyer refund (full cancellation) ───────────────────────────

    [Fact]
    public async Task BuyerRefund_AboveThreshold_ReturnsRefundOutcome()
    {
        var svc = NewService();
        var decision = await svc.ResolveBuyerRefundAsync(BlockchainNetwork.TRON, totalPaid: 102m, gasFee: 1m, default);

        Assert.Equal(RefundOutcome.Refund, decision.Outcome);
        Assert.Equal(101m, decision.NetRefund);
        Assert.Equal(2m, decision.Threshold);
        Assert.Equal(1m, decision.GasFee);
        Assert.Equal(102m, decision.TotalPaid);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public async Task BuyerRefund_ExactlyAtThreshold_ReturnsRefundOutcome()
    {
        // 09 §14.4 boundary: net == threshold ⇒ refund (≥, not >).
        var svc = NewService();
        var decision = await svc.ResolveBuyerRefundAsync(BlockchainNetwork.TRON, totalPaid: 3m, gasFee: 1m, default);

        Assert.Equal(RefundOutcome.Refund, decision.Outcome);
        Assert.Equal(2m, decision.NetRefund);
        Assert.Equal(2m, decision.Threshold);
    }

    [Fact]
    public async Task BuyerRefund_OneMicroUnitBelowThreshold_ReturnsBlock()
    {
        var svc = NewService();
        var decision = await svc.ResolveBuyerRefundAsync(BlockchainNetwork.TRON, totalPaid: 2.999999m, gasFee: 1m, default);

        Assert.Equal(RefundOutcome.Block, decision.Outcome);
        Assert.Equal(RefundBlockedReason.BelowMinimumThreshold, decision.Reason);
        Assert.Equal(1.999999m, decision.NetRefund);
        Assert.Equal(2m, decision.Threshold);
    }

    [Fact]
    public async Task BuyerRefund_NetGoesNegative_ReturnsBlockWithNegativeAmountReason()
    {
        // gasFee = 5, totalPaid = 3 ⇒ net = -2 ⇒ blocked, reason = NegativeAmount.
        var svc = NewService();
        var decision = await svc.ResolveBuyerRefundAsync(BlockchainNetwork.TRON, totalPaid: 3m, gasFee: 5m, default);

        Assert.Equal(RefundOutcome.Block, decision.Outcome);
        Assert.Equal(RefundBlockedReason.NegativeAmount, decision.Reason);
        Assert.Equal(-2m, decision.NetRefund);
        Assert.Equal(10m, decision.Threshold);
    }

    [Fact]
    public async Task BuyerRefund_ZeroGasFee_ReturnsRefundEvenForZeroTotal()
    {
        // Edge: gas fee = 0 ⇒ threshold = 0; any non-negative totalPaid clears.
        var svc = NewService();
        var decision = await svc.ResolveBuyerRefundAsync(BlockchainNetwork.TRON, totalPaid: 0m, gasFee: 0m, default);

        Assert.Equal(RefundOutcome.Refund, decision.Outcome);
        Assert.Equal(0m, decision.NetRefund);
        Assert.Equal(0m, decision.Threshold);
    }

    [Fact]
    public async Task BuyerRefund_CustomMinRefundRatio_RespectsLiveSetting()
    {
        // Admin bumps min_refund_threshold_ratio to 5 — a refund that would
        // have cleared at 2× now blocks at 5×. This is the whole point of
        // the live SystemSetting read.
        var svc = NewService(new GasFeeSettings(0.10m, 5m, 2m, 0.50m, 10m));
        var decision = await svc.ResolveBuyerRefundAsync(BlockchainNetwork.TRON, totalPaid: 4m, gasFee: 1m, default);

        Assert.Equal(RefundOutcome.Block, decision.Outcome);
        Assert.Equal(RefundBlockedReason.BelowMinimumThreshold, decision.Reason);
        Assert.Equal(3m, decision.NetRefund);
        Assert.Equal(5m, decision.Threshold);
    }

    [Fact]
    public async Task BuyerRefund_NegativeTotalPaid_Throws()
    {
        var svc = NewService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.ResolveBuyerRefundAsync(BlockchainNetwork.TRON, totalPaid: -1m, gasFee: 0m, default));
    }

    [Fact]
    public async Task BuyerRefund_NegativeGasFee_Throws()
    {
        var svc = NewService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.ResolveBuyerRefundAsync(BlockchainNetwork.TRON, totalPaid: 100m, gasFee: -1m, default));
    }

    // ─── Overpayment refund ─────────────────────────────────────────

    [Fact]
    public async Task OverpaymentRefund_LargeOverpayment_ReturnsRefund()
    {
        // expected 100, received 110 ⇒ overpayment 10; gas 1; threshold 2; refund 9 ≥ 2.
        var svc = NewService();
        var decision = await svc.ResolveOverpaymentRefundAsync(
            BlockchainNetwork.TRON, expected: 100m, received: 110m, gasFee: 1m, default);

        Assert.Equal(RefundOutcome.Refund, decision.Outcome);
        Assert.Equal(9m, decision.NetRefund);
        Assert.Equal(2m, decision.Threshold);
        Assert.Equal(10m, decision.TotalPaid);
    }

    [Fact]
    public async Task OverpaymentRefund_SmallOverpayment_BlocksBelowThreshold()
    {
        // expected 100, received 102 ⇒ overpayment 2; gas 1; threshold 2; refund 1 < 2 ⇒ block.
        var svc = NewService();
        var decision = await svc.ResolveOverpaymentRefundAsync(
            BlockchainNetwork.TRON, expected: 100m, received: 102m, gasFee: 1m, default);

        Assert.Equal(RefundOutcome.Block, decision.Outcome);
        Assert.Equal(RefundBlockedReason.BelowMinimumThreshold, decision.Reason);
        Assert.Equal(1m, decision.NetRefund);
        Assert.Equal(2m, decision.Threshold);
        Assert.Equal(2m, decision.TotalPaid);
    }

    [Fact]
    public async Task OverpaymentRefund_NoOverpayment_ReturnsBlockWithNegativeAmount()
    {
        // received == expected ⇒ overpayment 0; gas 1; net = -1 ⇒ NegativeAmount.
        var svc = NewService();
        var decision = await svc.ResolveOverpaymentRefundAsync(
            BlockchainNetwork.TRON, expected: 100m, received: 100m, gasFee: 1m, default);

        Assert.Equal(RefundOutcome.Block, decision.Outcome);
        Assert.Equal(RefundBlockedReason.NegativeAmount, decision.Reason);
        Assert.Equal(-1m, decision.NetRefund);
    }

    [Fact]
    public async Task OverpaymentRefund_Underpayment_ClampsTo_NegativeNet()
    {
        // received < expected ⇒ CalculateOverpayment returns 0 (no overpay
        // to refund); the call still executes — overpayment refund is only
        // meaningful for received > expected, but the service must answer
        // gracefully when callers pass an exact / underpayment record.
        var svc = NewService();
        var decision = await svc.ResolveOverpaymentRefundAsync(
            BlockchainNetwork.TRON, expected: 100m, received: 95m, gasFee: 1m, default);

        Assert.Equal(RefundOutcome.Block, decision.Outcome);
        Assert.Equal(RefundBlockedReason.NegativeAmount, decision.Reason);
        Assert.Equal(0m, decision.TotalPaid);
        Assert.Equal(-1m, decision.NetRefund);
    }

    // ─── Seller payout (gas-fee protection) ─────────────────────────

    [Fact]
    public async Task SellerPayout_GasFeeBelowThreshold_PlatformAbsorbs()
    {
        // commission 2, ratio 0.10 ⇒ threshold 0.20; gasFee 0.15 ≤ 0.20 ⇒
        // payout = price (100m).
        var svc = NewService();
        var payout = await svc.ResolveSellerPayoutAsync(
            BlockchainNetwork.TRON, price: 100m, commissionAmount: 2m, gasFee: 0.15m, default);

        Assert.Equal(100m, payout);
    }

    [Fact]
    public async Task SellerPayout_GasFeeAtThreshold_PlatformAbsorbs()
    {
        // Boundary: gasFee == threshold ⇒ payout = price (≤ semantic).
        var svc = NewService();
        var payout = await svc.ResolveSellerPayoutAsync(
            BlockchainNetwork.TRON, price: 100m, commissionAmount: 2m, gasFee: 0.20m, default);

        Assert.Equal(100m, payout);
    }

    [Fact]
    public async Task SellerPayout_GasFeeAboveThreshold_DeductsExcessFromSeller()
    {
        // commission 2, ratio 0.10 ⇒ threshold 0.20; gasFee 0.50 ⇒
        // overage = 0.30; payout = 100 - 0.30 = 99.70.
        var svc = NewService();
        var payout = await svc.ResolveSellerPayoutAsync(
            BlockchainNetwork.TRON, price: 100m, commissionAmount: 2m, gasFee: 0.50m, default);

        Assert.Equal(99.70m, payout);
    }

    [Fact]
    public async Task SellerPayout_AdminRaisesProtectionRatio_PlatformAbsorbsMore()
    {
        // ratio 0.10 → 0.50 admin update; same gasFee 0.50 now ≤ threshold 1.0.
        var svc = NewService(new GasFeeSettings(0.50m, 2m, 2m, 0.50m, 10m));
        var payout = await svc.ResolveSellerPayoutAsync(
            BlockchainNetwork.TRON, price: 100m, commissionAmount: 2m, gasFee: 0.50m, default);

        Assert.Equal(100m, payout);
    }

    // ─── Provider invocation ────────────────────────────────────────

    [Fact]
    public async Task ResolveBuyerRefund_AlwaysReadsSettings()
    {
        var stub = new StubSettingsProvider();
        var svc = GasFeeSettingsBackedNetworkSettings.NewRefundDecisionService(stub);

        await svc.ResolveBuyerRefundAsync(BlockchainNetwork.TRON, 100m, 1m, default);
        await svc.ResolveBuyerRefundAsync(BlockchainNetwork.TRON, 100m, 1m, default);

        // No caching layer — every call re-reads (mirrors documented behaviour
        // so admin updates take effect on the next call).
        Assert.Equal(2, stub.CallCount);
    }

    // ─── T149: per network ───────────────────────────────────────────

    /// <summary>Per-network multipliers: TRON 2, Ethereum 5 — read from the network settings, not the legacy key.</summary>
    private sealed class PerNetworkRatios : Skinora.Transactions.Application.Networks.IBlockchainNetworkSettingsProvider
    {
        public List<BlockchainNetwork> Asked { get; } = [];

        public Task<Skinora.Transactions.Application.Networks.BlockchainNetworkSettings> GetAsync(
            BlockchainNetwork network, CancellationToken cancellationToken)
        {
            Asked.Add(network);
            var defaults = Skinora.Transactions.Application.Networks.BlockchainNetworkSettingsProvider.DefaultsFor(network);
            return Task.FromResult(defaults with
            {
                MinRefundThresholdRatio = network == BlockchainNetwork.ETHEREUM ? 5m : 2m,
            });
        }
    }

    [Fact]
    public async Task MinRefundThreshold_Is_Per_Network()
    {
        var ratios = new PerNetworkRatios();
        // The legacy provider says 9 — it must not be the one read.
        var svc = new RefundDecisionService(
            new StubSettingsProvider { Settings = new GasFeeSettings(0.10m, 9m, 2m, 0.50m, 10m) }, ratios);

        // net 3, gas 1: clears TRON's 2× (threshold 2) but not Ethereum's 5× (threshold 5).
        var tron = await svc.ResolveBuyerRefundAsync(BlockchainNetwork.TRON, 4m, 1m, default);
        var ethereum = await svc.ResolveBuyerRefundAsync(BlockchainNetwork.ETHEREUM, 4m, 1m, default);

        Assert.Equal(RefundOutcome.Refund, tron.Outcome);
        Assert.Equal(2m, tron.Threshold);
        Assert.Equal(RefundOutcome.Block, ethereum.Outcome);
        Assert.Equal(5m, ethereum.Threshold);
        Assert.Equal(RefundBlockedReason.BelowMinimumThreshold, ethereum.Reason);
        Assert.Equal([BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM], ratios.Asked);
    }

    [Fact]
    public async Task OverpaymentThreshold_Is_Per_Network()
    {
        var svc = new RefundDecisionService(new StubSettingsProvider(), new PerNetworkRatios());

        // overpayment 4, gas 1 → net 3: TRON threshold 2 → refund; Ethereum threshold 5 → block.
        var tron = await svc.ResolveOverpaymentRefundAsync(BlockchainNetwork.TRON, 100m, 104m, 1m, default);
        var ethereum = await svc.ResolveOverpaymentRefundAsync(BlockchainNetwork.ETHEREUM, 100m, 104m, 1m, default);

        Assert.Equal(RefundOutcome.Refund, tron.Outcome);
        Assert.Equal(RefundOutcome.Block, ethereum.Outcome);
    }

    [Theory]
    [InlineData("0.15")]   // below the platform's share
    [InlineData("0.50")]   // above it — on TRON the seller would bear 0.30
    [InlineData("4.53")]
    public async Task EthereumPayout_Platform_Absorbs_The_Fee_Seller_Gets_The_Full_Price(string gasFee)
    {
        // 11 §F8 K8 / 08 §3.7.7: on Ethereum nothing comes off the seller.
        var svc = NewService();
        var payout = await svc.ResolveSellerPayoutAsync(
            BlockchainNetwork.ETHEREUM, price: 100m, commissionAmount: 2m, gasFee: decimal.Parse(gasFee, System.Globalization.CultureInfo.InvariantCulture), default);

        Assert.Equal(100m, payout);
    }

    [Fact]
    public async Task TronPayout_Keeps_The_Protection_Split()
    {
        var svc = NewService();
        var payout = await svc.ResolveSellerPayoutAsync(
            BlockchainNetwork.TRON, price: 20m, commissionAmount: 0.40m, gasFee: 2.29m, default);

        // 02 §5 table: 20 − (2.29 − 0.04) = 17.75.
        Assert.Equal(17.75m, payout);
    }
}
