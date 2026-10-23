using System.Collections.Concurrent;
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
/// <para>
/// T170 — per network (TRON and Ethereum both watched), with the balance and
/// the threshold it was compared with (the per-network panels draw the line
/// from the setting, not from a number in the dashboard), and the time of the
/// last successful check (<c>hot-wallet-monitor-stale</c>: a monitor that
/// stopped reading is a watchman that never fires).
/// </para>
/// </summary>
public interface IHotWalletThresholdObserver
{
    /// <param name="network">The hot wallet's network.</param>
    /// <param name="asset">USDT / USDC (upper limit) or the gas asset, TRX (lower floor).</param>
    /// <param name="direction"><see cref="HotWalletMonitorService.DirectionUpper"/> or <see cref="HotWalletMonitorService.DirectionLower"/>.</param>
    /// <param name="breached">True while the balance is beyond the threshold.</param>
    void Report(BlockchainNetwork network, string asset, string direction, bool breached);

    /// <summary>T170 — the balance read, in whole units (USDT, TRX).</summary>
    void ReportBalance(BlockchainNetwork network, string asset, decimal balance);

    /// <summary>T170 — the threshold compared with; <c>null</c> when none is set (the series is removed).</summary>
    void ReportThreshold(BlockchainNetwork network, string asset, string direction, decimal? threshold);

    /// <summary>
    /// T170 — a pass of <paramref name="network"/> started. Seeds the last-check
    /// time once, so a pass that never succeeds still ages into the stale alarm.
    /// </summary>
    void ReportAttempt(BlockchainNetwork network);

    /// <summary>T170 — a pass of <paramref name="network"/> read the balances and compared every threshold.</summary>
    void ReportChecked(BlockchainNetwork network);
}

/// <summary>
/// Publishes <c>skinora_backend_hot_wallet_threshold_breached{network,asset,direction}</c>
/// and (T170) <c>skinora_backend_hot_wallet_balance{network,asset}</c>,
/// <c>skinora_backend_hot_wallet_threshold{network,asset,direction}</c>,
/// <c>skinora_backend_hot_wallet_last_check_timestamp_seconds{network}</c>.
/// </summary>
public sealed class PrometheusHotWalletThresholdObserver : IHotWalletThresholdObserver
{
    public static readonly Gauge Breached = Metrics.CreateGauge(
        "skinora_backend_hot_wallet_threshold_breached",
        "1 while the hot wallet is beyond a threshold (asset above hot_wallet_limit / gas asset below its floor), per network, asset and direction",
        new GaugeConfiguration { LabelNames = ["network", "asset", "direction"] });

    public static readonly Gauge Balance = Metrics.CreateGauge(
        "skinora_backend_hot_wallet_balance",
        "Hot wallet balance read by the hot wallet monitor, in whole units (USDT, USDC, TRX), per network and asset",
        new GaugeConfiguration { LabelNames = ["network", "asset"] });

    public static readonly Gauge Threshold = Metrics.CreateGauge(
        "skinora_backend_hot_wallet_threshold",
        "Threshold the hot wallet balance is compared with (hot_wallet_limit upper / gas floor lower), per network, asset and direction",
        new GaugeConfiguration { LabelNames = ["network", "asset", "direction"] });

    public static readonly Gauge LastCheck = Metrics.CreateGauge(
        "skinora_backend_hot_wallet_last_check_timestamp_seconds",
        "Unix time of the last hot wallet check that read the balances, per network (seeded with the first attempt)",
        new GaugeConfiguration { LabelNames = ["network"] });

    // Networks whose last-check series exists — static like the gauge itself.
    private static readonly ConcurrentDictionary<string, bool> Seeded = new(StringComparer.Ordinal);

    private readonly TimeProvider _clock;

    public PrometheusHotWalletThresholdObserver()
        : this(TimeProvider.System)
    {
    }

    public PrometheusHotWalletThresholdObserver(TimeProvider clock)
    {
        _clock = clock;
    }

    public void Report(BlockchainNetwork network, string asset, string direction, bool breached) =>
        Breached.WithLabels(network.ToString(), asset, direction.ToLowerInvariant()).Set(breached ? 1 : 0);

    public void ReportBalance(BlockchainNetwork network, string asset, decimal balance) =>
        Balance.WithLabels(network.ToString(), asset).Set((double)balance);

    public void ReportThreshold(BlockchainNetwork network, string asset, string direction, decimal? threshold)
    {
        var labels = new[] { network.ToString(), asset, direction.ToLowerInvariant() };
        if (threshold is { } value)
        {
            Threshold.WithLabels(labels).Set((double)value);
        }
        else
        {
            Threshold.RemoveLabelled(labels);
        }
    }

    public void ReportAttempt(BlockchainNetwork network)
    {
        var label = network.ToString();
        if (Seeded.TryAdd(label, true))
        {
            LastCheck.WithLabels(label).Set(Now());
        }
    }

    public void ReportChecked(BlockchainNetwork network)
    {
        var label = network.ToString();
        Seeded.TryAdd(label, true);
        LastCheck.WithLabels(label).Set(Now());
    }

    private double Now() => _clock.GetUtcNow().ToUnixTimeMilliseconds() / 1000d;
}
