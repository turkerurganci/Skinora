using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.API.Services.Reconciliation;
using Skinora.Platform.Application.Wallets;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Configuration;
using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.API.Services.HotWallet;

/// <summary>
/// Periodic hot wallet balance monitor (T77 — 05 §3.3). Mirrors the shape of
/// <see cref="ReconciliationService"/>: a single <c>RunAsync</c> entry point
/// invoked by a Hangfire wrapper; per-finding side effects are a
/// <c>HOT_WALLET_THRESHOLD_BREACHED</c> AuditLog row and a SignalR
/// <c>AdminHotWalletThresholdBreached</c> broadcast.
///
/// <para>
/// Three thresholds drive alerts. <c>hot_wallet_limit</c> (decimal, USDT
/// unit) is compared against the USDT and USDC balances separately — the
/// admin-tunable cap applies per stablecoin. <c>hot_wallet.trx_balance_minimum</c>
/// (decimal, TRX unit) is the gas floor; below it the alert direction is
/// <c>Lower</c> (TRON only — T170). The hot wallet address is deployment
/// configuration shared with the signer, per network
/// (<see cref="IPlatformWalletAddressProvider"/>: <c>HOT_WALLET_ADDRESS</c>,
/// <c>ETHEREUM_HOT_WALLET_ADDRESS</c>) — until a network's address is set the
/// job emits a warn log and skips that network.
/// </para>
/// </summary>
public interface IHotWalletMonitorService
{
    Task<HotWalletMonitorOutcome> RunAsync(CancellationToken cancellationToken);
}

public sealed record HotWalletMonitorOutcome(
    bool HotWalletChecked,
    int BreachCount,
    long? BlockNumber);

/// <remarks>
/// <para>
/// T170 — one pass per network that has a service (05 §3.3a, the
/// <c>ReconciliationService</c> shape): each network's hot wallet is read from
/// its own service and compared with <c>hot_wallet_limit</c> (USDT / USDC,
/// 6 decimals on both networks — 4000 per network, T169). Before T170 only
/// TRON was watched (DEFERRED_BACKLOG <c>EthereumHotWalletUsdtLimitUnmonitored</c>):
/// USDT piling up in the Ethereum hot wallet above the limit reached nobody.
/// </para>
/// <para>
/// The gas floor stays per network where its alarm already lives: TRON's TRX
/// floor (<c>hot_wallet.trx_balance_minimum</c>, in SUN —
/// <see cref="BlockchainNetworkPolicy"/>) is checked here; Ethereum's hot and
/// fuel wallet ETH floors are watched by the Ethereum service itself
/// (<c>ethereum-wallet-eth-low</c>, T153) and are not repeated here, so one
/// low balance raises one alarm.
/// </para>
/// <para>
/// A network whose service is down or whose hot address is unset aborts only
/// its own pass. With a single checked network the outcome is that pass's;
/// with several, counts are summed and <c>BlockNumber</c> is null (block
/// heights of different chains do not compare).
/// </para>
/// </remarks>
public sealed class HotWalletMonitorService : IHotWalletMonitorService
{
    public const string HotWalletLimitKey = "hot_wallet_limit";
    public const string TrxBalanceMinimumKey = "hot_wallet.trx_balance_minimum";

    public const string DirectionUpper = "Upper";
    public const string DirectionLower = "Lower";

    public const string TokenTrx = "TRX";
    private const int StablecoinDecimals = 6;
    // SUN → TRX: the network policy's gas-asset scale (T148), not a literal.
    private static readonly int TrxDecimals = BlockchainNetworkPolicy.For(BlockchainNetwork.TRON).NativeAssetDecimals;
    private static readonly decimal StablecoinScale = (decimal)Math.Pow(10, StablecoinDecimals);
    private static readonly decimal TrxScale = (decimal)Math.Pow(10, TrxDecimals);

    private static readonly StablecoinType[] StablecoinTokens =
    [
        StablecoinType.USDT,
        StablecoinType.USDC,
    ];

    private readonly AppDbContext _db;
    private readonly IBlockchainServiceRegistry _services;
    private readonly INotificationRealtimePublisher _realtime;
    private readonly IPlatformWalletAddressProvider _walletAddresses;
    private readonly TimeProvider _clock;
    private readonly IHotWalletThresholdObserver _thresholds;
    private readonly ILogger<HotWalletMonitorService> _logger;

    public HotWalletMonitorService(
        AppDbContext db,
        IBlockchainServiceRegistry services,
        INotificationRealtimePublisher realtime,
        IPlatformWalletAddressProvider walletAddresses,
        TimeProvider clock,
        IHotWalletThresholdObserver thresholds,
        ILogger<HotWalletMonitorService> logger)
    {
        _db = db;
        _services = services;
        _realtime = realtime;
        _walletAddresses = walletAddresses;
        _clock = clock;
        _thresholds = thresholds;
        _logger = logger;
    }

    /// <summary>Whether <paramref name="network"/>'s gas floor is checked by this monitor (TRON only — see remarks).</summary>
    public static bool ChecksGasFloor(BlockchainNetwork network) => network == BlockchainNetwork.TRON;

    public async Task<HotWalletMonitorOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var settings = await ReadSettingsAsync(cancellationToken);
        var outcomes = new List<HotWalletMonitorOutcome>();
        foreach (var network in _services.ConfiguredNetworks)
        {
            outcomes.Add(await RunForNetworkAsync(network, settings, cancellationToken));
        }

        return outcomes.Count switch
        {
            0 => new HotWalletMonitorOutcome(false, 0, null),
            1 => outcomes[0],
            _ => new HotWalletMonitorOutcome(
                outcomes.Any(o => o.HotWalletChecked),
                outcomes.Sum(o => o.BreachCount),
                null),
        };
    }

    private async Task<HotWalletMonitorOutcome> RunForNetworkAsync(
        BlockchainNetwork network,
        HotWalletMonitorSettings settings,
        CancellationToken cancellationToken)
    {
        // T170 — the attempt is reported before anything can fail, so a pass
        // that never succeeds still ages into hot-wallet-monitor-stale.
        _thresholds.ReportAttempt(network);

        var hotWalletAddress = _walletAddresses.For(network).HotWalletAddress;
        if (string.IsNullOrWhiteSpace(hotWalletAddress))
        {
            _logger.LogWarning(
                "HotWalletMonitor skipping {Network}: its hot wallet address ({Key}) is unconfigured.",
                network, HotWalletKeyFor(network));
            return new HotWalletMonitorOutcome(false, 0, null);
        }

        var snapshot = await _services.Sidecar(network).GetWalletBalancesAsync(
            new[] { hotWalletAddress }, cancellationToken);
        if (snapshot.Status != BlockchainSidecarStatus.Success || snapshot.Balances is null)
        {
            _logger.LogWarning(
                "HotWalletMonitor {Network} aborted: its service returned {Status}.", network, snapshot.Status);
            return new HotWalletMonitorOutcome(false, 0, snapshot.BlockNumber);
        }

        var balances = snapshot.Balances
            // Canonical comparison (06 §1.4, T145) — case-insensitive for an
            // EVM address, exact for TRON base58.
            .FirstOrDefault(b => BlockchainAddress.AreEqual(b.Address, hotWalletAddress));
        if (balances is null)
        {
            _logger.LogWarning(
                "HotWalletMonitor {Network} snapshot missing address {Address}.",
                network, hotWalletAddress);
            return new HotWalletMonitorOutcome(false, 0, snapshot.BlockNumber);
        }

        var detectedAt = _clock.GetUtcNow().UtcDateTime;
        var breaches = 0;

        // T165 — every threshold reported on every successful run (0 when
        // within, or when no limit is set), so the alarm also resolves.
        // T170 — with the balance and the threshold beside it, for the
        // per-network panels.
        var stablecoinLimit = settings.HotWalletLimit is > 0m ? settings.HotWalletLimit : null;
        foreach (var token in StablecoinTokens)
        {
            var actual = ReadStablecoinAmount(balances.Tokens, token);
            var over = stablecoinLimit is { } limit && actual > limit;
            _thresholds.Report(network, token.ToString(), DirectionUpper, over);
            _thresholds.ReportBalance(network, token.ToString(), actual);
            _thresholds.ReportThreshold(network, token.ToString(), DirectionUpper, stablecoinLimit);

            if (over)
            {
                await RecordBreachAsync(
                    network: network,
                    tokenLabel: token.ToString(),
                    direction: DirectionUpper,
                    threshold: stablecoinLimit!.Value,
                    actual: actual,
                    blockNumber: snapshot.BlockNumber,
                    detectedAt: detectedAt,
                    cancellationToken: cancellationToken);
                breaches++;
            }
        }

        if (ChecksGasFloor(network))
        {
            var trxMinimum = settings.TrxMinimum is > 0m ? settings.TrxMinimum : null;
            var trxActual = ReadTrxAmount(balances.Tokens);
            var below = trxMinimum is { } floor && trxActual < floor;
            _thresholds.Report(network, TokenTrx, DirectionLower, below);
            _thresholds.ReportBalance(network, TokenTrx, trxActual);
            _thresholds.ReportThreshold(network, TokenTrx, DirectionLower, trxMinimum);

            if (below)
            {
                await RecordBreachAsync(
                    network: network,
                    tokenLabel: TokenTrx,
                    direction: DirectionLower,
                    threshold: trxMinimum!.Value,
                    actual: trxActual,
                    blockNumber: snapshot.BlockNumber,
                    detectedAt: detectedAt,
                    cancellationToken: cancellationToken);
                breaches++;
            }
        }

        _thresholds.ReportChecked(network);

        if (breaches == 0)
        {
            _logger.LogInformation(
                "HotWalletMonitor {Network} run complete: hot wallet within configured thresholds (block {Block}).",
                network, snapshot.BlockNumber);
        }
        else
        {
            _logger.LogWarning(
                "HotWalletMonitor {Network} run complete with {BreachCount} threshold breach(es) (block {Block}).",
                network, breaches, snapshot.BlockNumber);
        }

        return new HotWalletMonitorOutcome(true, breaches, snapshot.BlockNumber);
    }

    private static string HotWalletKeyFor(BlockchainNetwork network) => network == BlockchainNetwork.TRON
        ? EnvPlatformWalletAddressProvider.HotWalletConfigurationKey
        : EnvPlatformWalletAddressProvider.EthereumHotWalletConfigurationKey;

    private async Task RecordBreachAsync(
        BlockchainNetwork network,
        string tokenLabel,
        string direction,
        decimal threshold,
        decimal actual,
        long? blockNumber,
        DateTime detectedAt,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            // T170 — the network the hot wallet belongs to (both are watched).
            network = network.ToString(),
            token = tokenLabel,
            direction,
            threshold = threshold.ToString(CultureInfo.InvariantCulture),
            actual = actual.ToString(CultureInfo.InvariantCulture),
            blockNumber,
        });

        _db.Set<AuditLog>().Add(new AuditLog
        {
            UserId = null,
            ActorId = SeedConstants.SystemUserId,
            ActorType = ActorType.SYSTEM,
            Action = AuditAction.HOT_WALLET_THRESHOLD_BREACHED,
            EntityType = "HotWallet",
            EntityId = tokenLabel,
            OldValue = threshold.ToString(CultureInfo.InvariantCulture),
            NewValue = payload,
            CreatedAt = detectedAt,
        });
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "HotWalletMonitor breach network={Network} token={Token} direction={Direction} threshold={Threshold} actual={Actual} block={Block}.",
            network, tokenLabel, direction, threshold, actual, blockNumber);

        await _realtime.PublishAdminHotWalletThresholdBreachedAsync(
            new NotificationRealtimePayloads.AdminHotWalletThresholdBreached(
                Token: tokenLabel,
                Direction: direction,
                Threshold: threshold,
                Actual: actual,
                BlockNumber: blockNumber,
                DetectedAt: detectedAt,
                Network: network.ToString()),
            cancellationToken);
    }

    private async Task<HotWalletMonitorSettings> ReadSettingsAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == HotWalletLimitKey
                        || s.Key == TrxBalanceMinimumKey)
            .Select(s => new { s.Key, s.Value, s.IsConfigured })
            .ToListAsync(cancellationToken);

        // The addresses are configuration (per network, read in the pass), the
        // two thresholds stay admin-tunable (05 §3.3, owner decision 2026-09-16).
        decimal? hotWalletLimit = null;
        decimal? trxMinimum = null;
        foreach (var row in rows)
        {
            if (!row.IsConfigured || string.IsNullOrWhiteSpace(row.Value)) continue;
            var trimmed = row.Value!.Trim();
            if (string.Equals(trimmed, "NONE", StringComparison.Ordinal)) continue;

            if (row.Key == HotWalletLimitKey
                     && decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var limit))
            {
                hotWalletLimit = limit;
            }
            else if (row.Key == TrxBalanceMinimumKey
                     && decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var trx))
            {
                trxMinimum = trx;
            }
        }
        return new HotWalletMonitorSettings(hotWalletLimit, trxMinimum);
    }

    private static decimal ReadStablecoinAmount(
        IReadOnlyDictionary<string, string> tokens, StablecoinType token)
    {
        if (!tokens.TryGetValue(token.ToString(), out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return 0m;
        }
        if (!decimal.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawValue))
        {
            return 0m;
        }
        return Math.Round(rawValue / StablecoinScale, StablecoinDecimals, MidpointRounding.ToZero);
    }

    private static decimal ReadTrxAmount(IReadOnlyDictionary<string, string> tokens)
    {
        if (!tokens.TryGetValue(TokenTrx, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return 0m;
        }
        if (!decimal.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawValue))
        {
            return 0m;
        }
        // TRX is reported in SUN by TronGrid — same 6-decimal scale as
        // stablecoins (1 TRX = 1_000_000 SUN). Truncate at scale 6.
        return Math.Round(rawValue / TrxScale, TrxDecimals, MidpointRounding.ToZero);
    }

    private sealed record HotWalletMonitorSettings(
        decimal? HotWalletLimit,
        decimal? TrxMinimum);
}
