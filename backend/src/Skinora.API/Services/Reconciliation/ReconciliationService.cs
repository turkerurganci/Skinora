using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Payments.Domain.Entities;
using Skinora.Platform.Domain.Entities;
using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Reconciliation;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.API.Services.Reconciliation;

/// <summary>
/// Compares on-chain balances against the platform ledger (T76 — 05 §3.3).
///
/// <para>
/// Reconciliation runs once per day from the recurring
/// <see cref="ReconciliationJob"/>. Three scopes are compared:
/// </para>
/// <list type="bullet">
///   <item><b>DepositAddress</b> — every <see cref="PaymentAddress"/> whose
///   monitoring is still active. Expected = sum of CONFIRMED inflows minus
///   CONFIRMED outflows recorded in <see cref="BlockchainTransaction"/>.</item>
///   <item><b>HotWallet</b> — the address stored in the
///   <c>reconciliation.hot_wallet_address</c> SystemSetting. Expected =
///   CONFIRMED SWEEP inflows minus outbound transfers and minus hot→cold
///   ledger transfers.</item>
///   <item><b>ColdWallet</b> — the address stored in the
///   <c>reconciliation.cold_wallet_address</c> SystemSetting. Expected =
///   sum of <see cref="ColdWalletTransfer"/> inflows. MVP has no
///   cold→external outflow path.</item>
/// </list>
///
/// <para>
/// TRX is not reconciled; the platform has no TRX-denominated ledger.
/// Stablecoin scope is fixed at USDT + USDC per 08 §3.3 token allowlist.
/// </para>
///
/// <para>
/// Tolerance is zero (05 §3.3 financial calculation invariant). To avoid
/// false positives during in-flight payments, only <c>Status = CONFIRMED</c>
/// blockchain transactions count toward the expected total; pending /
/// detected rows are excluded so a payment mid-finalization does not show
/// up as a deposit "shortfall".
/// </para>
/// </summary>
public sealed class ReconciliationService : IReconciliationService
{
    public const string HotWalletAddressKey = "reconciliation.hot_wallet_address";
    public const string ColdWalletAddressKey = "reconciliation.cold_wallet_address";

    /// <summary>
    /// Sidecar accepts at most 100 addresses per request. Reconcile up to
    /// 98 deposit addresses in a single sweep — the remaining two slots
    /// cover the hot and cold wallet. If more deposits are active the run
    /// processes the oldest ones first and logs a warning so an operator
    /// can adjust cadence.
    /// </summary>
    public const int MaxDepositAddressesPerRun = 98;

    /// <summary>Supported stablecoin tokens (08 §3.3 allowlist).</summary>
    public static readonly IReadOnlyList<StablecoinType> SupportedTokens =
    [
        StablecoinType.USDT,
        StablecoinType.USDC,
    ];

    /// <summary>
    /// TRC-20 token decimals — both USDT and USDC are 6 on Tron (08 §3.4).
    /// Raw uint values from TronGrid are divided by 10^6 before comparison.
    /// </summary>
    private const int TokenDecimals = 6;

    private static readonly decimal TokenScale = (decimal)Math.Pow(10, TokenDecimals);

    /// <summary>
    /// Outbound BlockchainTransaction types for the hot wallet — these are
    /// the on-chain transfers that draw the hot wallet down.
    /// <see cref="BlockchainTransactionType.SWEEP"/> is treated as an
    /// <i>inflow</i> for the hot wallet (deposit → hot).
    /// </summary>
    private static readonly BlockchainTransactionType[] HotWalletOutboundTypes =
    [
        BlockchainTransactionType.SELLER_PAYOUT,
        BlockchainTransactionType.BUYER_REFUND,
        BlockchainTransactionType.EXCESS_REFUND,
        BlockchainTransactionType.WRONG_TOKEN_REFUND,
        BlockchainTransactionType.LATE_PAYMENT_REFUND,
        BlockchainTransactionType.INCORRECT_AMOUNT_REFUND,
    ];

    /// <summary>
    /// Inbound BlockchainTransaction types at a deposit address — payments
    /// observed by the monitor (08 §3.4).
    /// </summary>
    private static readonly BlockchainTransactionType[] DepositInflowTypes =
    [
        BlockchainTransactionType.BUYER_PAYMENT,
        BlockchainTransactionType.WRONG_TOKEN_INCOMING,
        BlockchainTransactionType.SPAM_TOKEN_INCOMING,
    ];

    /// <summary>
    /// Outbound BlockchainTransaction types at a deposit address — any
    /// transfer that leaves the deposit (sweep to hot, direct refund to
    /// the buyer when sweep has not yet happened, late refunds, ...).
    /// </summary>
    private static readonly BlockchainTransactionType[] DepositOutflowTypes =
    [
        BlockchainTransactionType.SWEEP,
        BlockchainTransactionType.BUYER_REFUND,
        BlockchainTransactionType.EXCESS_REFUND,
        BlockchainTransactionType.WRONG_TOKEN_REFUND,
        BlockchainTransactionType.LATE_PAYMENT_REFUND,
        BlockchainTransactionType.INCORRECT_AMOUNT_REFUND,
    ];

    private readonly AppDbContext _db;
    private readonly IBlockchainSidecarClient _sidecar;
    private readonly INotificationRealtimePublisher _realtime;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(
        AppDbContext db,
        IBlockchainSidecarClient sidecar,
        INotificationRealtimePublisher realtime,
        TimeProvider clock,
        ILogger<ReconciliationService> logger)
    {
        _db = db;
        _sidecar = sidecar;
        _realtime = realtime;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ReconciliationOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var settings = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == HotWalletAddressKey || s.Key == ColdWalletAddressKey)
            .Select(s => new { s.Key, s.Value, s.IsConfigured })
            .ToListAsync(cancellationToken);

        string? hotWallet = null;
        string? coldWallet = null;
        foreach (var row in settings)
        {
            var value = row.IsConfigured && !string.IsNullOrWhiteSpace(row.Value)
                ? row.Value!.Trim()
                : null;
            // String settings use the documented "NONE" sentinel for "not set"
            // (auth.banned_countries pattern, 06 §3.17 + T63a maintenance
            // string columns). Production deploy replaces NONE with the real
            // Tron address; until then we treat it as unconfigured.
            if (string.Equals(value, "NONE", StringComparison.Ordinal)) value = null;
            if (row.Key == HotWalletAddressKey) hotWallet = value;
            if (row.Key == ColdWalletAddressKey) coldWallet = value;
        }

        if (string.IsNullOrEmpty(hotWallet))
        {
            _logger.LogWarning(
                "Reconciliation skipping hot wallet scope: {Key} is unconfigured.",
                HotWalletAddressKey);
        }
        if (string.IsNullOrEmpty(coldWallet))
        {
            _logger.LogInformation(
                "Reconciliation skipping cold wallet scope: {Key} is unconfigured.",
                ColdWalletAddressKey);
        }

        var depositAddresses = await _db.Set<PaymentAddress>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(pa => !pa.IsDeleted && pa.MonitoringStatus != MonitoringStatus.STOPPED)
            .OrderBy(pa => pa.CreatedAt)
            .Select(pa => new { pa.Id, pa.Address })
            .Take(MaxDepositAddressesPerRun + 1)
            .ToListAsync(cancellationToken);

        var truncated = false;
        if (depositAddresses.Count > MaxDepositAddressesPerRun)
        {
            truncated = true;
            depositAddresses = depositAddresses
                .Take(MaxDepositAddressesPerRun)
                .ToList();
            _logger.LogWarning(
                "Reconciliation deposit address list truncated to {Cap} oldest active addresses; consider lowering cadence.",
                MaxDepositAddressesPerRun);
        }

        var addresses = new List<string>(depositAddresses.Count + 2);
        foreach (var deposit in depositAddresses) addresses.Add(deposit.Address);
        if (!string.IsNullOrEmpty(hotWallet)) addresses.Add(hotWallet);
        if (!string.IsNullOrEmpty(coldWallet) && coldWallet != hotWallet)
        {
            addresses.Add(coldWallet);
        }

        if (addresses.Count == 0)
        {
            _logger.LogInformation(
                "Reconciliation run skipped — no active deposit addresses and no wallet settings configured.");
            return new ReconciliationOutcome(0, false, false, 0, null);
        }

        var snapshot = await _sidecar.GetWalletBalancesAsync(addresses, cancellationToken);
        if (snapshot.Status != BlockchainSidecarStatus.Success || snapshot.Balances is null)
        {
            _logger.LogWarning(
                "Reconciliation aborted: sidecar returned {Status}.", snapshot.Status);
            return new ReconciliationOutcome(0, false, false, 0, null);
        }

        var snapshotByAddress = snapshot.Balances
            .GroupBy(b => b.Address, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Tokens, StringComparer.Ordinal);

        var detectedAt = _clock.GetUtcNow().UtcDateTime;
        var totalMismatches = 0;

        foreach (var deposit in depositAddresses)
        {
            if (!snapshotByAddress.TryGetValue(deposit.Address, out var tokens))
            {
                _logger.LogWarning(
                    "Reconciliation snapshot missing deposit address {Address}.", deposit.Address);
                continue;
            }
            totalMismatches += await ReconcileDepositAddressAsync(
                deposit.Id, deposit.Address, tokens,
                snapshot.BlockNumber, detectedAt, cancellationToken);
        }

        var hotChecked = false;
        if (!string.IsNullOrEmpty(hotWallet)
            && snapshotByAddress.TryGetValue(hotWallet, out var hotTokens))
        {
            totalMismatches += await ReconcileHotWalletAsync(
                hotWallet, hotTokens, snapshot.BlockNumber, detectedAt, cancellationToken);
            hotChecked = true;
        }

        var coldChecked = false;
        if (!string.IsNullOrEmpty(coldWallet)
            && snapshotByAddress.TryGetValue(coldWallet, out var coldTokens))
        {
            totalMismatches += await ReconcileColdWalletAsync(
                coldWallet, coldTokens, snapshot.BlockNumber, detectedAt, cancellationToken);
            coldChecked = true;
        }

        if (totalMismatches == 0)
        {
            _logger.LogInformation(
                "Reconciliation run complete: {Deposits} deposit address(es) + hot:{Hot} + cold:{Cold} all balanced (block {Block}{Truncated}).",
                depositAddresses.Count, hotChecked, coldChecked, snapshot.BlockNumber,
                truncated ? ", truncated" : string.Empty);
        }
        else
        {
            _logger.LogWarning(
                "Reconciliation run complete with {MismatchCount} mismatch(es) across {Deposits} deposit address(es) + hot:{Hot} + cold:{Cold} (block {Block}).",
                totalMismatches, depositAddresses.Count, hotChecked, coldChecked, snapshot.BlockNumber);
        }

        return new ReconciliationOutcome(
            depositAddresses.Count,
            hotChecked,
            coldChecked,
            totalMismatches,
            snapshot.BlockNumber);
    }

    private async Task<int> ReconcileDepositAddressAsync(
        Guid paymentAddressId,
        string address,
        IReadOnlyDictionary<string, string> onChainTokens,
        long? blockNumber,
        DateTime detectedAt,
        CancellationToken cancellationToken)
    {
        var ledgerRows = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(bx => bx.PaymentAddressId == paymentAddressId
                         && bx.Status == BlockchainTransactionStatus.CONFIRMED)
            .GroupBy(bx => new { bx.Type, bx.Token })
            .Select(g => new LedgerSummaryRow(
                g.Key.Type, g.Key.Token, g.Sum(x => x.Amount)))
            .ToListAsync(cancellationToken);

        var expectedByToken = ComputeSignedExpected(ledgerRows,
            inflowTypes: DepositInflowTypes,
            outflowTypes: DepositOutflowTypes);

        return await EvaluateAndRecordAsync(
            scope: ReconciliationScope.DepositAddress,
            address: address,
            expectedByToken: expectedByToken,
            onChainTokens: onChainTokens,
            blockNumber: blockNumber,
            detectedAt: detectedAt,
            cancellationToken: cancellationToken);
    }

    private async Task<int> ReconcileHotWalletAsync(
        string hotWallet,
        IReadOnlyDictionary<string, string> onChainTokens,
        long? blockNumber,
        DateTime detectedAt,
        CancellationToken cancellationToken)
    {
        var ledgerRows = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(bx => bx.Status == BlockchainTransactionStatus.CONFIRMED
                         && ((bx.ToAddress == hotWallet && bx.Type == BlockchainTransactionType.SWEEP)
                             || (bx.FromAddress == hotWallet
                                 && HotWalletOutboundTypes.Contains(bx.Type))))
            .GroupBy(bx => new { bx.Type, bx.Token })
            .Select(g => new LedgerSummaryRow(
                g.Key.Type, g.Key.Token, g.Sum(x => x.Amount)))
            .ToListAsync(cancellationToken);

        var expectedByToken = ComputeSignedExpected(ledgerRows,
            inflowTypes: [BlockchainTransactionType.SWEEP],
            outflowTypes: HotWalletOutboundTypes);

        // Subtract every hot→cold transfer recorded in the ColdWalletTransfer
        // ledger. The cold scope below adds these back as inflow so the two
        // checks remain consistent.
        var coldTransfers = await _db.Set<ColdWalletTransfer>()
            .AsNoTracking()
            .Where(ct => ct.FromAddress == hotWallet)
            .GroupBy(ct => ct.Token)
            .Select(g => new { Token = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);
        foreach (var row in coldTransfers)
        {
            expectedByToken.TryGetValue(row.Token, out var current);
            expectedByToken[row.Token] = current - row.Total;
        }

        return await EvaluateAndRecordAsync(
            scope: ReconciliationScope.HotWallet,
            address: hotWallet,
            expectedByToken: expectedByToken,
            onChainTokens: onChainTokens,
            blockNumber: blockNumber,
            detectedAt: detectedAt,
            cancellationToken: cancellationToken);
    }

    private async Task<int> ReconcileColdWalletAsync(
        string coldWallet,
        IReadOnlyDictionary<string, string> onChainTokens,
        long? blockNumber,
        DateTime detectedAt,
        CancellationToken cancellationToken)
    {
        var inflow = await _db.Set<ColdWalletTransfer>()
            .AsNoTracking()
            .Where(ct => ct.ToAddress == coldWallet)
            .GroupBy(ct => ct.Token)
            .Select(g => new { Token = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var expectedByToken = new Dictionary<StablecoinType, decimal>();
        foreach (var row in inflow)
        {
            expectedByToken[row.Token] = row.Total;
        }

        return await EvaluateAndRecordAsync(
            scope: ReconciliationScope.ColdWallet,
            address: coldWallet,
            expectedByToken: expectedByToken,
            onChainTokens: onChainTokens,
            blockNumber: blockNumber,
            detectedAt: detectedAt,
            cancellationToken: cancellationToken);
    }

    private async Task<int> EvaluateAndRecordAsync(
        ReconciliationScope scope,
        string address,
        Dictionary<StablecoinType, decimal> expectedByToken,
        IReadOnlyDictionary<string, string> onChainTokens,
        long? blockNumber,
        DateTime detectedAt,
        CancellationToken cancellationToken)
    {
        var mismatchCount = 0;
        foreach (var token in SupportedTokens)
        {
            expectedByToken.TryGetValue(token, out var expected);
            var actual = ReadOnChainAmount(onChainTokens, token);
            if (expected == actual) continue;

            await RecordMismatchAsync(scope, address, token, expected, actual,
                blockNumber, detectedAt, cancellationToken);
            mismatchCount++;
        }
        return mismatchCount;
    }

    private async Task RecordMismatchAsync(
        ReconciliationScope scope,
        string address,
        StablecoinType token,
        decimal expected,
        decimal actual,
        long? blockNumber,
        DateTime detectedAt,
        CancellationToken cancellationToken)
    {
        var scopeLabel = scope.ToString();
        var tokenLabel = token.ToString();
        var delta = actual - expected;

        var payloadJson = JsonSerializer.Serialize(new
        {
            token = tokenLabel,
            expected = expected.ToString(CultureInfo.InvariantCulture),
            actual = actual.ToString(CultureInfo.InvariantCulture),
            delta = delta.ToString(CultureInfo.InvariantCulture),
            blockNumber,
        });

        var audit = new AuditLog
        {
            UserId = null,
            ActorId = SeedConstants.SystemUserId,
            ActorType = ActorType.SYSTEM,
            Action = AuditAction.RECONCILIATION_MISMATCH,
            EntityType = scopeLabel,
            EntityId = address,
            OldValue = expected.ToString(CultureInfo.InvariantCulture),
            NewValue = payloadJson,
            CreatedAt = detectedAt,
        };
        _db.Set<AuditLog>().Add(audit);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Reconciliation mismatch scope={Scope} address={Address} token={Token} expected={Expected} actual={Actual} delta={Delta} block={Block}",
            scopeLabel, address, tokenLabel, expected, actual, delta, blockNumber);

        await _realtime.PublishAdminReconciliationMismatchAsync(
            new NotificationRealtimePayloads.AdminReconciliationMismatch(
                Scope: scopeLabel,
                Address: address,
                Token: tokenLabel,
                Expected: expected,
                Actual: actual,
                Delta: delta,
                BlockNumber: blockNumber,
                DetectedAt: detectedAt),
            cancellationToken);
    }

    private static decimal ReadOnChainAmount(
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
        // 6-decimal tokens (USDT/USDC) — truncate at scale 6 (09 §14.3).
        return Math.Round(rawValue / TokenScale, TokenDecimals, MidpointRounding.ToZero);
    }

    private static Dictionary<StablecoinType, decimal> ComputeSignedExpected(
        IEnumerable<LedgerSummaryRow> ledgerRows,
        IReadOnlyList<BlockchainTransactionType> inflowTypes,
        IReadOnlyList<BlockchainTransactionType> outflowTypes)
    {
        var expected = new Dictionary<StablecoinType, decimal>();
        foreach (var row in ledgerRows)
        {
            var sign = inflowTypes.Contains(row.Type)
                ? 1m
                : outflowTypes.Contains(row.Type)
                    ? -1m
                    : 0m;
            if (sign == 0m) continue;

            expected.TryGetValue(row.Token, out var current);
            expected[row.Token] = current + (row.Total * sign);
        }
        return expected;
    }

    /// <summary>
    /// EF-projected ledger summary — (Type, Token, Total) tuple per
    /// CONFIRMED BlockchainTransaction group.
    /// </summary>
    private sealed record LedgerSummaryRow(
        BlockchainTransactionType Type,
        StablecoinType Token,
        decimal Total);
}
