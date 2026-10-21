using Skinora.Transactions.Tests.Helpers;
using Skinora.Transactions.Application.Networks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
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
        ChargedGasFeeResolverFactory.Create(
            FakeBlockchainServiceRegistry.ForTron(gasFee: estimator),
            new StubSettings { Cap = cap ?? MaxChargedFee });

    // -------------------- T148 — per-network estimate and fallback --------------------

    [Fact]
    public async Task Ethereum_Is_Priced_By_Ethereums_Service_Not_Trons()
    {
        var tron = new StubEstimator { Result = 2.40m };
        var ethereum = new StubEstimator { Result = 0.07m };
        var registry = FakeBlockchainServiceRegistry.ForTron(gasFee: tron)
            .With(BlockchainNetwork.ETHEREUM, gasFee: ethereum);
        var sut = ChargedGasFeeResolverFactory.Create(registry, new StubSettings());

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
        var sut = ChargedGasFeeResolverFactory.Create(
            FakeBlockchainServiceRegistry.ForTron(gasFee: tron),
            new StubSettings());

        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(EthereumRefundFallback, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.StaticFallback, resolved.Source);
        Assert.Empty(tron.Requests);
    }

    private const decimal EthereumRefundFallback = 0.50m;

    // -------------------- T160b — retry on a retryable failure --------------------
    //
    // Fixture values are chosen apart from every static setting (TRON refund
    // 2.00, Ethereum refund 0.50, payouts 0.50) so a test that silently fell
    // back would show.

    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 1, 13, 0, TimeSpan.Zero);

    private static readonly GasFeeEstimateRetryPolicy NoWaitRetry =
        new(MaxAttempts: 3, Delays: [TimeSpan.Zero], Budget: TimeSpan.FromSeconds(12));

    [Fact]
    public async Task T160b_A_Retryable_Failure_Is_Asked_Again_And_The_Estimate_Is_Charged()
    {
        // T159b B1: 502 FEE_ESTIMATE_SIMULATION_FAILED (block not found),
        // retryable: true — the second call answers.
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Failed(retryable: true, "HTTP 502 FEE_ESTIMATE_SIMULATION_FAILED"),
            GasFeeEstimateAttempt.Estimated(0.18m));
        var observer = new RecordingObserver();
        var sut = BuildEthereum(estimator, NoWaitRetry, observer: observer);

        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(0.18m, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.RuntimeEstimate, resolved.Source);
        Assert.Equal(2, estimator.Calls);
        Assert.Empty(observer.Fallbacks);
    }

    [Fact]
    public async Task T160b_Tron_Refund_Is_Retried_The_Same_Way()
    {
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Failed(retryable: true, "HttpRequestException"),
            GasFeeEstimateAttempt.Failed(retryable: true, "HTTP 500 INTERNAL_ERROR"),
            GasFeeEstimateAttempt.Estimated(2.31m));
        var sut = ChargedGasFeeResolverFactory.Create(
            FakeBlockchainServiceRegistry.ForTron(gasFee: estimator), new StubSettings(), NoWaitRetry);

        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.TRON, "TDeposit", "TBuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(2.31m, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.RuntimeEstimate, resolved.Source);
        Assert.Equal(3, estimator.Calls);
    }

    [Fact]
    public async Task T160b_The_Hot_Wallet_Refund_Is_Retried_Too()
    {
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Failed(retryable: true, "HTTP 502 RPC_UNAVAILABLE"),
            GasFeeEstimateAttempt.Estimated(0.03m));
        var sut = BuildEthereum(estimator, NoWaitRetry);

        var resolved = await sut.ResolveHotWalletRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(0.03m, resolved.FeeUsdt);
        Assert.Equal(2, estimator.Calls);
    }

    [Fact]
    public async Task T160b_A_Final_Failure_Is_Not_Retried()
    {
        // A refused request (400) or an answer that cannot be believed would
        // be the same answer again.
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Failed(retryable: false, "HTTP 400 INVALID_ESTIMATE_REQUEST"),
            GasFeeEstimateAttempt.Estimated(0.18m));
        var sut = BuildEthereum(estimator, NoWaitRetry);

        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(EthereumRefundFallback, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.StaticFallback, resolved.Source);
        Assert.Equal(1, estimator.Calls);
    }

    [Fact]
    public async Task T160b_Gives_Up_After_The_Last_Attempt_And_The_Alarm_Is_Told()
    {
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Failed(retryable: true, "a"),
            GasFeeEstimateAttempt.Failed(retryable: true, "b"),
            GasFeeEstimateAttempt.Failed(retryable: true, "c"),
            GasFeeEstimateAttempt.Estimated(0.18m));
        var observer = new RecordingObserver();
        var sut = BuildEthereum(estimator, NoWaitRetry, observer: observer);

        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(EthereumRefundFallback, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.StaticFallback, resolved.Source);
        Assert.Equal(3, estimator.Calls);
        Assert.Equal(
            (BlockchainNetwork.ETHEREUM, ChargedGasFeeResolver.RefundFlow, GasFeeFallbackReason.Unavailable, false),
            Assert.Single(observer.Fallbacks));
    }

    [Fact]
    public async Task T160b_The_Production_Policy_Waits_1s_Then_3s_Between_Calls()
    {
        var fake = new FakeTimeProvider(T0);
        var clock = new TimerRecordingClock(fake);
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Failed(retryable: true, "a"),
            GasFeeEstimateAttempt.Failed(retryable: true, "b"),
            GasFeeEstimateAttempt.Estimated(0.18m))
        { Clock = fake };
        var sut = BuildEthereum(estimator, GasFeeEstimateRetryPolicy.Default, clock);

        var resolved = await DriveAsync(fake, sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None));

        Assert.Equal(0.18m, resolved.FeeUsdt);
        Assert.Equal(3, estimator.Calls);
        // The waits (the other timers are the retries' budget deadlines, 7.5 s or more).
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)],
            clock.DueTimes.Where(t => t <= TimeSpan.FromSeconds(3)));
        Assert.All(clock.DueTimes.Where(t => t > TimeSpan.FromSeconds(3)),
            t => Assert.InRange(t, TimeSpan.FromSeconds(7.5), TimeSpan.FromSeconds(11)));
    }

    [Fact]
    public async Task T160b_A_First_Attempt_That_Used_The_Budget_Is_Not_Retried()
    {
        // The estimate client's own timeout (30 s) already passed: a retry
        // would only add to what the caller waits. Worst case stays one
        // client timeout, as before T160b.
        var clock = new FakeTimeProvider(T0);
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Failed(retryable: true, "TaskCanceledException"),
            GasFeeEstimateAttempt.Estimated(0.18m))
        {
            Clock = clock,
            Durations = [TimeSpan.FromSeconds(30)],
        };
        var sut = BuildEthereum(estimator, NoWaitRetry, clock);

        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(EthereumRefundFallback, resolved.FeeUsdt);
        Assert.Equal(1, estimator.Calls);
    }

    [Fact]
    public async Task T160b_A_Retry_Still_Running_At_The_Budget_Is_Cancelled_And_The_Fallback_Charged()
    {
        var clock = new FakeTimeProvider(T0);
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Failed(retryable: true, "HTTP 502"),
            null) // the second call hangs until its token is cancelled
        {
            Clock = clock,
            Durations = [TimeSpan.FromSeconds(2)],
        };
        var sut = BuildEthereum(estimator, NoWaitRetry, clock);

        var resolved = await DriveAsync(clock, sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None));

        Assert.Equal(EthereumRefundFallback, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.StaticFallback, resolved.Source);
        Assert.Equal(2, estimator.Calls);
        // Cut at the 12 s budget, counted from the first call.
        Assert.Equal(TimeSpan.FromSeconds(12), estimator.CancelledAt.Single() - T0);
    }

    [Fact]
    public async Task T160b_The_Callers_Cancellation_Still_Propagates()
    {
        var estimator = new ScriptedEstimator(GasFeeEstimateAttempt.Failed(retryable: true, "HTTP 502"));
        var sut = BuildEthereum(estimator, GasFeeEstimateRetryPolicy.Default);
        using var cts = new CancellationTokenSource();
        estimator.OnCall = cts.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, cts.Token));
    }

    [Fact]
    public async Task T160b_The_Payout_Keeps_One_Call_And_Its_Static_Fallback()
    {
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Failed(retryable: true, "HTTP 502"),
            GasFeeEstimateAttempt.Estimated(0.04m));
        var observer = new RecordingObserver();
        var sut = BuildEthereum(estimator, NoWaitRetry, observer: observer);

        var resolved = await sut.ResolvePayoutFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xseller", 20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(BlockchainNetworkSettingsProvider.EthereumDefaults.PayoutGasFeeFallbackUsdt, resolved.FeeUsdt);
        Assert.Equal(1, estimator.Calls);
        Assert.Equal(
            (BlockchainNetwork.ETHEREUM, ChargedGasFeeResolver.PayoutFlow, GasFeeFallbackReason.Unavailable, false),
            Assert.Single(observer.Fallbacks));
    }

    // -------------------- T160b — the fallback is the last measured estimate --------------------

    [Fact]
    public async Task T160b_The_Refund_Fallback_Charges_The_Last_Measured_Estimate_While_Fresh()
    {
        var clock = new FakeTimeProvider(T0);
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Estimated(0.06m),
            GasFeeEstimateAttempt.Failed(retryable: false, "HTTP 502"));
        var observer = new RecordingObserver();
        var sut = BuildEthereum(estimator, NoWaitRetry, clock, observer: observer);

        await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdepositA", "0xbuyerA", 10.20m, StablecoinType.USDT, CancellationToken.None);
        clock.Advance(MeasuredGasFeeCache.Ttl);
        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdepositB", "0xbuyerB", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(0.06m, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.MeasuredFallback, resolved.Source);
        Assert.Equal(
            (BlockchainNetwork.ETHEREUM, ChargedGasFeeResolver.RefundFlow, GasFeeFallbackReason.Unavailable, true),
            Assert.Single(observer.Fallbacks));
    }

    [Fact]
    public async Task T160b_A_Measurement_Older_Than_Six_Hours_Is_Not_Used()
    {
        var clock = new FakeTimeProvider(T0);
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Estimated(0.06m),
            GasFeeEstimateAttempt.Failed(retryable: false, "HTTP 502"));
        var sut = BuildEthereum(estimator, NoWaitRetry, clock);

        await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdepositA", "0xbuyerA", 10.20m, StablecoinType.USDT, CancellationToken.None);
        clock.Advance(MeasuredGasFeeCache.Ttl + TimeSpan.FromSeconds(1));
        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdepositB", "0xbuyerB", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(EthereumRefundFallback, resolved.FeeUsdt);
        Assert.Equal(GasFeeSource.StaticFallback, resolved.Source);
    }

    [Fact]
    public async Task T160b_A_Measurement_Applies_Only_To_Its_Network_Sender_And_Token()
    {
        var clock = new FakeTimeProvider(T0);
        var ethereum = new ScriptedEstimator(GasFeeEstimateAttempt.Estimated(0.06m)) { Then = GasFeeEstimateAttempt.Failed(false, "down") };
        var tron = new ScriptedEstimator(GasFeeEstimateAttempt.Failed(false, "down"));
        var measured = new MeasuredGasFeeCache(clock);
        var sut = ChargedGasFeeResolverFactory.Create(
            FakeBlockchainServiceRegistry.ForTron(gasFee: tron).With(BlockchainNetwork.ETHEREUM, gasFee: ethereum),
            new StubSettings(), NoWaitRetry, clock, measured);

        // Measured: Ethereum, deposit sender, USDT.
        await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        var hotWallet = await sut.ResolveHotWalletRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);
        var otherToken = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDC, CancellationToken.None);
        var otherNetwork = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.TRON, "TDeposit", "TBuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);
        var sameKind = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit2", "0xbuyer2", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(EthereumRefundFallback, hotWallet.FeeUsdt);
        Assert.Equal(EthereumRefundFallback, otherToken.FeeUsdt);
        Assert.Equal(StaticRefundFee, otherNetwork.FeeUsdt);
        Assert.Equal(0.06m, sameKind.FeeUsdt);
    }

    [Fact]
    public async Task T160b_A_Refused_Estimate_Is_Never_A_Measurement()
    {
        var clock = new FakeTimeProvider(T0);
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Estimated(0.06m),
            GasFeeEstimateAttempt.Estimated(250m),          // above the 10 USDT ceiling
            GasFeeEstimateAttempt.Failed(retryable: false, "down"));
        var sut = BuildEthereum(estimator, NoWaitRetry, clock);

        await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);
        var refused = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);
        var afterwards = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(0.06m, refused.FeeUsdt);
        Assert.Equal(GasFeeSource.EstimateRejected, refused.Source);
        Assert.Equal(0.06m, afterwards.FeeUsdt);
        Assert.Equal(GasFeeSource.MeasuredFallback, afterwards.Source);
    }

    [Fact]
    public async Task T160b_A_Payout_Estimate_Prices_A_Hot_Wallet_Refund_Fallback_But_The_Payout_Fallback_Stays_Static()
    {
        var clock = new FakeTimeProvider(T0);
        var estimator = new ScriptedEstimator(GasFeeEstimateAttempt.Estimated(0.04m))
        {
            Then = GasFeeEstimateAttempt.Failed(retryable: false, "down"),
        };
        var sut = BuildEthereum(estimator, NoWaitRetry, clock);

        await sut.ResolvePayoutFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xseller", 20m, StablecoinType.USDT, CancellationToken.None);
        var refund = await sut.ResolveHotWalletRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);
        var payout = await sut.ResolvePayoutFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xseller2", 20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(0.04m, refund.FeeUsdt);
        Assert.Equal(GasFeeSource.MeasuredFallback, refund.Source);
        Assert.Equal(BlockchainNetworkSettingsProvider.EthereumDefaults.PayoutGasFeeFallbackUsdt, payout.FeeUsdt);
        Assert.Equal(GasFeeSource.StaticFallback, payout.Source);
    }

    [Fact]
    public async Task T160b_A_Refund_Without_A_Deposit_Address_Charges_The_Measured_Deposit_Refund_And_Is_Reported()
    {
        var clock = new FakeTimeProvider(T0);
        var estimator = new ScriptedEstimator(GasFeeEstimateAttempt.Estimated(0.06m));
        var observer = new RecordingObserver();
        var sut = BuildEthereum(estimator, NoWaitRetry, clock, observer: observer);

        await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);
        var resolved = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, null, "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(0.06m, resolved.FeeUsdt);
        Assert.Equal(1, estimator.Calls);
        Assert.Equal(
            (BlockchainNetwork.ETHEREUM, ChargedGasFeeResolver.RefundFlow, GasFeeFallbackReason.NoSender, true),
            Assert.Single(observer.Fallbacks));
    }

    // -------------------- T160b düzeltme turu B1 — min(measured, static) --------------------
    //
    // The measurement does not know the recipient's address state, so on its
    // own it could charge more than the setting did before T160b (T159b
    // Sepolia: 0.87 and 0.18 from one deposit). The setting caps it. Fixture
    // measurements sit on both sides of every static setting and equal none.

    [Fact]
    public async Task T160b_B1_A_Measurement_Above_The_Static_Setting_Is_Capped_By_The_Setting()
    {
        var clock = new FakeTimeProvider(T0);
        var ethereum = new ScriptedEstimator(GasFeeEstimateAttempt.Estimated(0.87m))
        {
            Then = GasFeeEstimateAttempt.Failed(retryable: false, "down"),
        };
        var tron = new ScriptedEstimator(GasFeeEstimateAttempt.Estimated(4.13m))
        {
            Then = GasFeeEstimateAttempt.Failed(retryable: false, "down"),
        };
        var observer = new RecordingObserver();
        var sut = ChargedGasFeeResolverFactory.Create(
            FakeBlockchainServiceRegistry.ForTron(gasFee: tron).With(BlockchainNetwork.ETHEREUM, gasFee: ethereum),
            new StubSettings(), NoWaitRetry, clock, observer: observer);

        var ethereumMeasured = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdepositA", "0xbuyerA", 10.20m, StablecoinType.USDT, CancellationToken.None);
        var tronMeasured = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.TRON, "TDepositA", "TBuyerA", 10.20m, StablecoinType.USDT, CancellationToken.None);
        var ethereumFallback = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdepositB", "0xbuyerB", 10.20m, StablecoinType.USDT, CancellationToken.None);
        var tronFallback = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.TRON, "TDepositB", "TBuyerB", 10.20m, StablecoinType.USDT, CancellationToken.None);
        var tronNoSender = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.TRON, null, "TBuyerC", 10.20m, StablecoinType.USDT, CancellationToken.None);

        // The measurements were charged as estimates (and recorded) ...
        Assert.Equal(0.87m, ethereumMeasured.FeeUsdt);
        Assert.Equal(4.13m, tronMeasured.FeeUsdt);
        // ... but a fallback never charges more than the setting.
        Assert.Equal(EthereumRefundFallback, ethereumFallback.FeeUsdt);
        Assert.Equal(GasFeeSource.StaticFallback, ethereumFallback.Source);
        Assert.Equal(StaticRefundFee, tronFallback.FeeUsdt);
        Assert.Equal(GasFeeSource.StaticFallback, tronFallback.Source);
        Assert.Equal(StaticRefundFee, tronNoSender.FeeUsdt);
        Assert.Equal(
            [
                (BlockchainNetwork.ETHEREUM, ChargedGasFeeResolver.RefundFlow, GasFeeFallbackReason.Unavailable, false),
                (BlockchainNetwork.TRON, ChargedGasFeeResolver.RefundFlow, GasFeeFallbackReason.Unavailable, false),
                (BlockchainNetwork.TRON, ChargedGasFeeResolver.RefundFlow, GasFeeFallbackReason.NoSender, false),
            ],
            observer.Fallbacks);
    }

    [Fact]
    public async Task T160b_B1_A_Measurement_Below_The_Static_Setting_Is_Charged()
    {
        var clock = new FakeTimeProvider(T0);
        var ethereum = new ScriptedEstimator(GasFeeEstimateAttempt.Estimated(0.18m))
        {
            Then = GasFeeEstimateAttempt.Failed(retryable: false, "down"),
        };
        var tron = new ScriptedEstimator(GasFeeEstimateAttempt.Estimated(1.37m))
        {
            Then = GasFeeEstimateAttempt.Failed(retryable: false, "down"),
        };
        var observer = new RecordingObserver();
        var sut = ChargedGasFeeResolverFactory.Create(
            FakeBlockchainServiceRegistry.ForTron(gasFee: tron).With(BlockchainNetwork.ETHEREUM, gasFee: ethereum),
            new StubSettings(), NoWaitRetry, clock, observer: observer);

        await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdepositA", "0xbuyerA", 10.20m, StablecoinType.USDT, CancellationToken.None);
        await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.TRON, "TDepositA", "TBuyerA", 10.20m, StablecoinType.USDT, CancellationToken.None);
        var ethereumFallback = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdepositB", "0xbuyerB", 10.20m, StablecoinType.USDT, CancellationToken.None);
        var tronFallback = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.TRON, "TDepositB", "TBuyerB", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(0.18m, ethereumFallback.FeeUsdt);
        Assert.Equal(GasFeeSource.MeasuredFallback, ethereumFallback.Source);
        Assert.Equal(1.37m, tronFallback.FeeUsdt);
        Assert.Equal(GasFeeSource.MeasuredFallback, tronFallback.Source);
        Assert.Equal(
            [
                (BlockchainNetwork.ETHEREUM, ChargedGasFeeResolver.RefundFlow, GasFeeFallbackReason.Unavailable, true),
                (BlockchainNetwork.TRON, ChargedGasFeeResolver.RefundFlow, GasFeeFallbackReason.Unavailable, true),
            ],
            observer.Fallbacks);
    }

    [Fact]
    public async Task T160b_B1_A_Refused_Estimate_After_A_Measurement_Above_The_Setting_Charges_The_Setting()
    {
        var clock = new FakeTimeProvider(T0);
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Estimated(0.87m),
            GasFeeEstimateAttempt.Estimated(250m)); // above the 10 USDT ceiling
        var sut = BuildEthereum(estimator, NoWaitRetry, clock);

        await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);
        var refused = await sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None);

        Assert.Equal(EthereumRefundFallback, refused.FeeUsdt);
        Assert.Equal(GasFeeSource.EstimateRejected, refused.Source);
    }

    // -------------------- T160b düzeltme turu B4 — the check before each wait --------------------

    [Fact]
    public async Task T160b_B4_No_Wait_Starts_When_The_Retry_Could_Not_Start_Before_The_Budget_Ends()
    {
        // The first call failed fast-enough to be retryable but late: at 10 s,
        // with a 3 s wait before the next call, the retry would start at 13 s,
        // past the 12 s budget. The fallback is charged at once — no wait.
        var fake = new FakeTimeProvider(T0);
        var clock = new TimerRecordingClock(fake);
        var estimator = new ScriptedEstimator(
            GasFeeEstimateAttempt.Failed(retryable: true, "HTTP 502"),
            GasFeeEstimateAttempt.Estimated(0.18m))
        {
            Clock = fake,
            Durations = [TimeSpan.FromSeconds(10)],
        };
        var policy = new GasFeeEstimateRetryPolicy(
            MaxAttempts: 3, Delays: [TimeSpan.FromSeconds(3)], Budget: TimeSpan.FromSeconds(12));
        var sut = BuildEthereum(estimator, policy, clock);

        var resolved = await DriveAsync(fake, sut.ResolveRefundFeeAsync(
            BlockchainNetwork.ETHEREUM, "0xdeposit", "0xbuyer", 10.20m, StablecoinType.USDT, CancellationToken.None));

        Assert.Equal(EthereumRefundFallback, resolved.FeeUsdt);
        Assert.Equal(1, estimator.Calls);
        Assert.Empty(clock.DueTimes);                                   // no wait was started
        Assert.Equal(T0 + TimeSpan.FromSeconds(10), fake.GetUtcNow());  // returned when the first call ended
    }

    private static ChargedGasFeeResolver BuildEthereum(
        IGasFeeEstimator estimator,
        GasFeeEstimateRetryPolicy policy,
        TimeProvider? clock = null,
        RecordingObserver? observer = null) =>
        ChargedGasFeeResolverFactory.Create(
            new FakeBlockchainServiceRegistry().With(BlockchainNetwork.ETHEREUM, gasFee: estimator),
            new StubSettings(),
            policy,
            clock,
            observer: observer);

    // Runs a resolve that waits on fake time to its end, advancing the clock
    // in half-second steps while it is pending.
    private static async Task<T> DriveAsync<T>(FakeTimeProvider clock, Task<T> pending)
    {
        for (var i = 0; i < 200 && !pending.IsCompleted; i++)
        {
            await Task.Delay(5);
            if (!pending.IsCompleted) clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        return await pending;
    }

    /// <summary>
    /// Answers each call with the next scripted outcome (then <see cref="Then"/>);
    /// a <c>null</c> entry hangs until the call's token is cancelled. Optionally
    /// moves a fake clock forward by the call's duration.
    /// </summary>
    private sealed class ScriptedEstimator(params GasFeeEstimateAttempt?[] script) : IGasFeeEstimator
    {
        private int _next;

        public FakeTimeProvider? Clock { get; init; }
        public TimeSpan[] Durations { get; init; } = [];
        public GasFeeEstimateAttempt? Then { get; init; }
        public Action? OnCall { get; set; }
        public int Calls => CalledAt.Count;
        public List<DateTimeOffset> CalledAt { get; } = [];
        public List<DateTimeOffset> CancelledAt { get; } = [];

        public async Task<GasFeeEstimateAttempt> EstimateFeeAsync(
            GasFeeEstimateRequest request, CancellationToken cancellationToken)
        {
            var index = _next++;
            CalledAt.Add(Clock?.GetUtcNow() ?? DateTimeOffset.UtcNow);
            OnCall?.Invoke();
            if (index < Durations.Length) Clock!.Advance(Durations[index]);

            var outcome = index < script.Length ? script[index] : Then ?? script[^1];
            if (outcome is null)
            {
                var hung = new TaskCompletionSource();
                await using var registration = cancellationToken.Register(() =>
                {
                    CancelledAt.Add(Clock?.GetUtcNow() ?? DateTimeOffset.UtcNow);
                    hung.TrySetCanceled(cancellationToken);
                });
                await hung.Task;
            }
            return outcome!;
        }

        public async Task<decimal?> EstimateFeeUsdtAsync(
            GasFeeEstimateRequest request, CancellationToken cancellationToken) =>
            (await EstimateFeeAsync(request, cancellationToken)).FeeUsdt;

        public Task<SweepCostEstimate?> EstimateSweepCostAsync(
            StablecoinType token, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("ChargedGasFeeResolver must not ask for a sweep estimate.");
    }

    /// <summary>A fake clock that also records the due time of every timer it is asked for.</summary>
    private sealed class TimerRecordingClock(FakeTimeProvider inner) : TimeProvider
    {
        public List<TimeSpan> DueTimes { get; } = [];

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

        public override long GetTimestamp() => inner.GetTimestamp();

        public override long TimestampFrequency => inner.TimestampFrequency;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (DueTimes) DueTimes.Add(dueTime);
            return inner.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed class RecordingObserver : IGasFeeFallbackObserver
    {
        public List<(BlockchainNetwork, string, GasFeeFallbackReason, bool)> Fallbacks { get; } = [];

        public void FallbackCharged(BlockchainNetwork network, string flow, GasFeeFallbackReason reason, bool measured) =>
            Fallbacks.Add((network, flow, reason, measured));
    }

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

        // T149 — the resolver never prices a sweep; reaching this is a bug.
        public Task<SweepCostEstimate?> EstimateSweepCostAsync(
            StablecoinType token, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("ChargedGasFeeResolver must not ask for a sweep estimate.");
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
