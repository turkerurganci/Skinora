using Prometheus;
using Skinora.Shared.Enums;

namespace Skinora.API.Services.HotWallet;

/// <summary>
/// T165 — the hot wallet thresholds as a metric, so a breach reaches the
/// operator outside the panel. Before T165 a breach was an AuditLog row plus a
/// transient SignalR banner for whoever had the admin panel open — nothing
/// arrived anywhere else. The monitor reports every threshold on every
/// successful run (1 breached, 0 within), and the
/// <c>hot-wallet-threshold-breached</c> money alarm fires on it. A failed run
/// (service unreachable, address unset) reports nothing: the last value stays.
/// </summary>
public interface IHotWalletThresholdObserver
{
    /// <param name="network">The hot wallet's network (today TRON only — DEFERRED_BACKLOG EthereumHotWalletUsdtLimitUnmonitored, T170).</param>
    /// <param name="asset">USDT / USDC (upper limit) or the gas asset, TRX (lower floor).</param>
    /// <param name="direction"><see cref="HotWalletMonitorService.DirectionUpper"/> or <see cref="HotWalletMonitorService.DirectionLower"/>.</param>
    /// <param name="breached">True while the balance is beyond the threshold.</param>
    void Report(BlockchainNetwork network, string asset, string direction, bool breached);
}

/// <summary>Publishes <c>skinora_backend_hot_wallet_threshold_breached{network,asset,direction}</c>.</summary>
public sealed class PrometheusHotWalletThresholdObserver : IHotWalletThresholdObserver
{
    public static readonly Gauge Breached = Metrics.CreateGauge(
        "skinora_backend_hot_wallet_threshold_breached",
        "1 while the hot wallet is beyond a threshold (asset above hot_wallet_limit / gas asset below its floor), per network, asset and direction",
        new GaugeConfiguration { LabelNames = ["network", "asset", "direction"] });

    public void Report(BlockchainNetwork network, string asset, string direction, bool breached) =>
        Breached.WithLabels(network.ToString(), asset, direction.ToLowerInvariant()).Set(breached ? 1 : 0);
}
