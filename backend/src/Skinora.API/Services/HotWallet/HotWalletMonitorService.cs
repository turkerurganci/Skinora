using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.API.Services.Reconciliation;
using Skinora.Platform.Domain.Entities;
using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
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
/// <c>Lower</c>. The hot wallet address itself ships unconfigured — until
/// the operator sets <c>reconciliation.hot_wallet_address</c> the job emits
/// a warn log and exits cleanly.
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

public sealed class HotWalletMonitorService : IHotWalletMonitorService
{
    public const string HotWalletLimitKey = "hot_wallet_limit";
    public const string TrxBalanceMinimumKey = "hot_wallet.trx_balance_minimum";

    public const string DirectionUpper = "Upper";
    public const string DirectionLower = "Lower";

    public const string TokenTrx = "TRX";
    private const int StablecoinDecimals = 6;
    private const int TrxDecimals = 6;
    private static readonly decimal StablecoinScale = (decimal)Math.Pow(10, StablecoinDecimals);
    private static readonly decimal TrxScale = (decimal)Math.Pow(10, TrxDecimals);

    private static readonly StablecoinType[] StablecoinTokens =
    [
        StablecoinType.USDT,
        StablecoinType.USDC,
    ];

    private readonly AppDbContext _db;
    private readonly IBlockchainSidecarClient _sidecar;
    private readonly INotificationRealtimePublisher _realtime;
    private readonly TimeProvider _clock;
    private readonly ILogger<HotWalletMonitorService> _logger;

    public HotWalletMonitorService(
        AppDbContext db,
        IBlockchainSidecarClient sidecar,
        INotificationRealtimePublisher realtime,
        TimeProvider clock,
        ILogger<HotWalletMonitorService> logger)
    {
        _db = db;
        _sidecar = sidecar;
        _realtime = realtime;
        _clock = clock;
        _logger = logger;
    }

    public async Task<HotWalletMonitorOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var settings = await ReadSettingsAsync(cancellationToken);
        if (settings.HotWalletAddress is null)
        {
            _logger.LogWarning(
                "HotWalletMonitor skipping run: {Key} is unconfigured.",
                ReconciliationService.HotWalletAddressKey);
            return new HotWalletMonitorOutcome(false, 0, null);
        }

        var snapshot = await _sidecar.GetWalletBalancesAsync(
            new[] { settings.HotWalletAddress }, cancellationToken);
        if (snapshot.Status != BlockchainSidecarStatus.Success || snapshot.Balances is null)
        {
            _logger.LogWarning(
                "HotWalletMonitor aborted: sidecar returned {Status}.", snapshot.Status);
            return new HotWalletMonitorOutcome(false, 0, snapshot.BlockNumber);
        }

        var balances = snapshot.Balances
            .FirstOrDefault(b => string.Equals(b.Address, settings.HotWalletAddress, StringComparison.Ordinal));
        if (balances is null)
        {
            _logger.LogWarning(
                "HotWalletMonitor snapshot missing address {Address}.",
                settings.HotWalletAddress);
            return new HotWalletMonitorOutcome(false, 0, snapshot.BlockNumber);
        }

        var detectedAt = _clock.GetUtcNow().UtcDateTime;
        var breaches = 0;

        if (settings.HotWalletLimit is { } stablecoinLimit && stablecoinLimit > 0m)
        {
            foreach (var token in StablecoinTokens)
            {
                var actual = ReadStablecoinAmount(balances.Tokens, token);
                if (actual > stablecoinLimit)
                {
                    await RecordBreachAsync(
                        tokenLabel: token.ToString(),
                        direction: DirectionUpper,
                        threshold: stablecoinLimit,
                        actual: actual,
                        blockNumber: snapshot.BlockNumber,
                        detectedAt: detectedAt,
                        cancellationToken: cancellationToken);
                    breaches++;
                }
            }
        }

        if (settings.TrxMinimum is { } trxMinimum && trxMinimum > 0m)
        {
            var trxActual = ReadTrxAmount(balances.Tokens);
            if (trxActual < trxMinimum)
            {
                await RecordBreachAsync(
                    tokenLabel: TokenTrx,
                    direction: DirectionLower,
                    threshold: trxMinimum,
                    actual: trxActual,
                    blockNumber: snapshot.BlockNumber,
                    detectedAt: detectedAt,
                    cancellationToken: cancellationToken);
                breaches++;
            }
        }

        if (breaches == 0)
        {
            _logger.LogInformation(
                "HotWalletMonitor run complete: hot wallet within configured thresholds (block {Block}).",
                snapshot.BlockNumber);
        }
        else
        {
            _logger.LogWarning(
                "HotWalletMonitor run complete with {BreachCount} threshold breach(es) (block {Block}).",
                breaches, snapshot.BlockNumber);
        }

        return new HotWalletMonitorOutcome(true, breaches, snapshot.BlockNumber);
    }

    private async Task RecordBreachAsync(
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
            "HotWalletMonitor breach token={Token} direction={Direction} threshold={Threshold} actual={Actual} block={Block}.",
            tokenLabel, direction, threshold, actual, blockNumber);

        await _realtime.PublishAdminHotWalletThresholdBreachedAsync(
            new NotificationRealtimePayloads.AdminHotWalletThresholdBreached(
                Token: tokenLabel,
                Direction: direction,
                Threshold: threshold,
                Actual: actual,
                BlockNumber: blockNumber,
                DetectedAt: detectedAt),
            cancellationToken);
    }

    private async Task<HotWalletMonitorSettings> ReadSettingsAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == ReconciliationService.HotWalletAddressKey
                        || s.Key == HotWalletLimitKey
                        || s.Key == TrxBalanceMinimumKey)
            .Select(s => new { s.Key, s.Value, s.IsConfigured })
            .ToListAsync(cancellationToken);

        string? hotWalletAddress = null;
        decimal? hotWalletLimit = null;
        decimal? trxMinimum = null;
        foreach (var row in rows)
        {
            if (!row.IsConfigured || string.IsNullOrWhiteSpace(row.Value)) continue;
            var trimmed = row.Value!.Trim();
            if (string.Equals(trimmed, "NONE", StringComparison.Ordinal)) continue;

            if (row.Key == ReconciliationService.HotWalletAddressKey)
            {
                hotWalletAddress = trimmed;
            }
            else if (row.Key == HotWalletLimitKey
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
        return new HotWalletMonitorSettings(hotWalletAddress, hotWalletLimit, trxMinimum);
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
        string? HotWalletAddress,
        decimal? HotWalletLimit,
        decimal? TrxMinimum);
}
