using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Tests.Helpers;

namespace Skinora.Transactions.Tests.Unit.GasFee;

/// <summary>
/// T149 — the buyer's network fee line (11 §F8 K8, 08 §3.1a): Ethereum is 0
/// without asking anyone; TRON is the sweep estimate × (1 + buffer) rounded up;
/// an unavailable estimate writes the per-network fallback (never a wait); an
/// estimate above the sanity ceiling is refused; the estimate is cached so the
/// quote shown and the line fixed come from the same one, while the settings
/// are read every time.
/// </summary>
public class NetworkFeeQuoteServiceTests
{
    private const long SweepCostSun = 8_441_350;     // 02 §5 example basis
    private const decimal TrxPrice = 0.34m;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly StubSweepEstimator _estimator = new() { Result = new SweepCostEstimate(SweepCostSun, TrxPrice) };
    private readonly StubSettings _settings = new();
    private readonly FakeBlockchainServiceRegistry _services;
    private readonly SweepCostEstimateCache _cache;

    public NetworkFeeQuoteServiceTests()
    {
        _services = FakeBlockchainServiceRegistry.ForTron(gasFee: _estimator);
        _cache = new SweepCostEstimateCache(_clock);
    }

    private NetworkFeeQuoteService Sut() =>
        new(_services, _settings, _cache, NullLogger<NetworkFeeQuoteService>.Instance);

    [Fact]
    public async Task Ethereum_Is_Zero_And_Asks_No_Service()
    {
        var quote = await Sut().GetBuyerNetworkFeeAsync(BlockchainNetwork.ETHEREUM, StablecoinType.USDT, default);

        Assert.Equal(0m, quote.Amount);
        Assert.Equal(NetworkFeeSource.NotCharged, quote.Source);
        Assert.Empty(_services.Requests);
        Assert.Equal(0, _estimator.Calls);
    }

    [Fact]
    public async Task Tron_Is_The_Estimate_Times_Buffer_Rounded_Up()
    {
        var quote = await Sut().GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        Assert.Equal(2.90m, quote.Amount);
        Assert.Equal(NetworkFeeSource.RuntimeEstimate, quote.Source);
        Assert.Equal([("GasFee", BlockchainNetwork.TRON)], _services.Requests);
        Assert.Equal([StablecoinType.USDT], _estimator.Tokens);
    }

    [Fact]
    public async Task Tron_Uses_The_Live_Buffer_Setting()
    {
        _settings.Buffer = 0.10m;

        var quote = await Sut().GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        // 8.44135 × 0.34 = 2.870059 × 1.10 = 3.157065 → 3.16.
        Assert.Equal(3.16m, quote.Amount);
    }

    [Fact]
    public async Task Tron_Without_An_Estimate_Writes_The_Fallback_Without_Waiting()
    {
        _estimator.Result = null;

        var quote = await Sut().GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        Assert.Equal(3.00m, quote.Amount);
        Assert.Equal(NetworkFeeSource.StaticFallback, quote.Source);
    }

    [Fact]
    public async Task Tron_Fallback_Is_Rounded_Up_To_The_Cent()
    {
        _estimator.Result = null;
        _settings.Fallback = 2.901m;

        var quote = await Sut().GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        Assert.Equal(2.91m, quote.Amount);
    }

    [Fact]
    public async Task Tron_Network_Without_A_Service_Writes_The_Fallback()
    {
        var unconfigured = new FakeBlockchainServiceRegistry(); // TRON not configured → fail-closed service
        var sut = new NetworkFeeQuoteService(
            unconfigured, _settings, _cache, NullLogger<NetworkFeeQuoteService>.Instance);

        var quote = await sut.GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        Assert.Equal(3.00m, quote.Amount);
        Assert.Equal(NetworkFeeSource.StaticFallback, quote.Source);
    }

    [Fact]
    public async Task Tron_Estimate_Above_The_Ceiling_Is_Refused_Not_Clamped()
    {
        // 100 TRX × 0.34 = 34 > 10 → refused, fallback written.
        _estimator.Result = new SweepCostEstimate(100_000_000, TrxPrice);

        var quote = await Sut().GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        Assert.Equal(3.00m, quote.Amount);
        Assert.Equal(NetworkFeeSource.EstimateRejected, quote.Source);
    }

    [Fact]
    public async Task Tron_Estimate_Exactly_At_The_Ceiling_Is_Kept()
    {
        // 10 TRX × 1.00 = 10.00 with no buffer == ceiling 10 → kept (> refuses, = does not).
        _settings.Buffer = 0m;
        _estimator.Result = new SweepCostEstimate(10_000_000, 1m);

        var quote = await Sut().GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        Assert.Equal(10.00m, quote.Amount);
        Assert.Equal(NetworkFeeSource.RuntimeEstimate, quote.Source);
    }

    [Fact]
    public async Task Quote_And_Acceptance_Within_Five_Minutes_Share_One_Estimate()
    {
        var sut = Sut();
        var shown = await sut.GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        // The price moves at the sidecar, but within the TTL the cached
        // estimate answers: the buyer is charged what they were shown.
        _estimator.Result = new SweepCostEstimate(SweepCostSun, 0.40m);
        _clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        var fixedAtAccept = await sut.GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        Assert.Equal(shown.Amount, fixedAtAccept.Amount);
        Assert.Equal(1, _estimator.Calls);
    }

    [Fact]
    public async Task Estimate_Is_Asked_Again_After_Five_Minutes()
    {
        var sut = Sut();
        await sut.GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        _estimator.Result = new SweepCostEstimate(SweepCostSun, 0.40m);
        _clock.Advance(TimeSpan.FromMinutes(5));
        var later = await sut.GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        // 8.44135 × 0.40 = 3.37654 × 1.007 = 3.400176 → 3.41.
        Assert.Equal(3.41m, later.Amount);
        Assert.Equal(2, _estimator.Calls);
    }

    [Fact]
    public async Task A_Failed_Estimate_Is_Remembered_For_One_Minute_Only()
    {
        _estimator.Result = null;
        var sut = Sut();
        await sut.GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);
        _clock.Advance(TimeSpan.FromSeconds(59));
        await sut.GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);
        Assert.Equal(1, _estimator.Calls);

        _estimator.Result = new SweepCostEstimate(SweepCostSun, TrxPrice);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var recovered = await sut.GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        Assert.Equal(2.90m, recovered.Amount);
        Assert.Equal(2, _estimator.Calls);
    }

    [Fact]
    public async Task Settings_Are_Read_Live_Even_When_The_Estimate_Is_Cached()
    {
        var sut = Sut();
        await sut.GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        _settings.Buffer = 0.10m;
        var after = await sut.GetBuyerNetworkFeeAsync(BlockchainNetwork.TRON, StablecoinType.USDT, default);

        Assert.Equal(3.16m, after.Amount);
        Assert.Equal(1, _estimator.Calls);
    }

    private sealed class StubSweepEstimator : IGasFeeEstimator
    {
        public SweepCostEstimate? Result { get; set; }
        public int Calls { get; private set; }
        public List<StablecoinType> Tokens { get; } = [];

        public Task<decimal?> EstimateFeeUsdtAsync(GasFeeEstimateRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The fee line never asks for a transfer estimate.");

        public Task<SweepCostEstimate?> EstimateSweepCostAsync(StablecoinType token, CancellationToken cancellationToken)
        {
            Calls++;
            Tokens.Add(token);
            return Task.FromResult(Result);
        }
    }

    private sealed class StubSettings : IBlockchainNetworkSettingsProvider
    {
        public decimal Buffer { get; set; } = 0.007m;
        public decimal Fallback { get; set; } = 3.00m;

        public Task<BlockchainNetworkSettings> GetAsync(BlockchainNetwork network, CancellationToken cancellationToken) =>
            Task.FromResult(network == BlockchainNetwork.TRON
                ? BlockchainNetworkSettingsProvider.TronDefaults with
                {
                    NetworkFeeBufferRatio = Buffer,
                    NetworkFeeFallbackUsdt = Fallback,
                }
                : BlockchainNetworkSettingsProvider.DefaultsFor(network));
    }
}
