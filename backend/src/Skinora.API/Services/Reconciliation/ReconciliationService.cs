using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Payments.Domain.Entities;
using Skinora.Platform.Application.Wallets;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Configuration;
using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Networks;
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
///   <item><b>HotWallet</b> — the address from
///   <see cref="IPlatformWalletAddressProvider"/> (<c>HOT_WALLET_ADDRESS</c>).
///   Expected = CONFIRMED SWEEP inflows minus outbound transfers and minus
///   hot→cold ledger transfers.</item>
///   <item><b>ColdWallet</b> — the address from
///   <see cref="IPlatformWalletAddressProvider"/> (<c>COLD_WALLET_ADDRESS</c>).
///   Expected = sum of <see cref="ColdWalletTransfer"/> inflows. MVP has no
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
    /// Deposit → hot wallet types, the hot wallet's inflow: the sale's
    /// <see cref="BlockchainTransactionType.SWEEP"/> and (T160c) the
    /// collection of withheld refund fees.
    /// </summary>
    private static readonly BlockchainTransactionType[] HotWalletInflowTypes =
    [
        BlockchainTransactionType.SWEEP,
        BlockchainTransactionType.REFUND_FEE_SWEEP,
    ];

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
        // T160c — collection of withheld refund fees (deposit → hot).
        BlockchainTransactionType.REFUND_FEE_SWEEP,
        BlockchainTransactionType.BUYER_REFUND,
        BlockchainTransactionType.EXCESS_REFUND,
        BlockchainTransactionType.WRONG_TOKEN_REFUND,
        BlockchainTransactionType.LATE_PAYMENT_REFUND,
        BlockchainTransactionType.INCORRECT_AMOUNT_REFUND,
    ];

    private readonly AppDbContext _db;
    private readonly IBlockchainServiceRegistry _services;
    private readonly INotificationRealtimePublisher _realtime;
    private readonly IPlatformWalletAddressProvider _walletAddresses;
    private readonly IReconciliationRunObserver _runObserver;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(
        AppDbContext db,
        IBlockchainServiceRegistry services,
        INotificationRealtimePublisher realtime,
        IPlatformWalletAddressProvider walletAddresses,
        IReconciliationRunObserver runObserver,
        IOutboxService outbox,
        TimeProvider clock,
        ILogger<ReconciliationService> logger)
    {
        _db = db;
        _services = services;
        _realtime = realtime;
        _walletAddresses = walletAddresses;
        _runObserver = runObserver;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    /// <remarks>
    /// T148 — one pass per network that has a service (05 §3.3a): that
    /// network's deposits, hot and cold wallets, balances read from that
    /// network's service. A network whose service is down aborts only its own
    /// pass. With a single configured network the outcome is that pass's;
    /// with several, counts are summed and <c>BlockNumber</c> is null (block
    /// heights of different chains do not compare).
    /// </remarks>
    public async Task<ReconciliationOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var outcomes = new List<ReconciliationOutcome>();
        foreach (var network in _services.ConfiguredNetworks)
        {
            outcomes.Add(await RunForNetworkAsync(network, cancellationToken));
        }

        return outcomes.Count switch
        {
            0 => new ReconciliationOutcome(0, false, false, 0, null),
            1 => outcomes[0],
            _ => new ReconciliationOutcome(
                outcomes.Sum(o => o.DepositAddressesChecked),
                outcomes.Any(o => o.HotWalletChecked),
                outcomes.Any(o => o.ColdWalletChecked),
                outcomes.Sum(o => o.MismatchCount),
                null),
        };
    }

    private async Task<ReconciliationOutcome> RunForNetworkAsync(
        BlockchainNetwork network, CancellationToken cancellationToken)
    {
        // Deployment configuration, not an admin-editable setting: the same
        // env var the signer pins its destinations to (05 §3.3, owner decision
        // 2026-09-16). Unset behaves exactly as the old "NONE" sentinel did.
        var wallets = _walletAddresses.For(network);
        var hotWallet = wallets.HotWalletAddress;
        var coldWallet = wallets.ColdWalletAddress;

        if (string.IsNullOrEmpty(hotWallet))
        {
            _logger.LogWarning(
                "Reconciliation skipping {Network} hot wallet scope: {Key} is unconfigured.",
                network, HotWalletKeyFor(network));
        }
        if (string.IsNullOrEmpty(coldWallet))
        {
            _logger.LogInformation(
                "Reconciliation skipping {Network} cold wallet scope: {Key} is unconfigured.",
                network, ColdWalletKeyFor(network));
        }

        var depositAddresses = await _db.Set<PaymentAddress>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(pa => !pa.IsDeleted
                && pa.MonitoringStatus != MonitoringStatus.STOPPED
                && pa.Network == network)
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
        if (!string.IsNullOrEmpty(coldWallet) && !BlockchainAddress.AreEqual(coldWallet, hotWallet))
        {
            addresses.Add(coldWallet);
        }

        if (addresses.Count == 0)
        {
            _logger.LogInformation(
                "Reconciliation run skipped — no active deposit addresses and no wallet settings configured.");
            return new ReconciliationOutcome(0, false, false, 0, null);
        }

        var snapshot = await ReadBalancesAsync(network, addresses, cancellationToken);
        if (snapshot.Status != BlockchainSidecarStatus.Success || snapshot.Balances is null)
        {
            // T158 (i) — not a mismatch, but the network's books went unchecked
            // this round: the admin alarm (reconciliation-network-failed) sees it.
            _logger.LogError(
                "Reconciliation of {Network} aborted: its service returned {Status} — this round did not check the network's balances (admin alarm).",
                network, snapshot.Status);
            _runObserver.Report(network, succeeded: false);
            return new ReconciliationOutcome(0, false, false, 0, null);
        }
        _runObserver.Report(network, succeeded: true);

        // Canonical comparer (06 §1.4, T145): the sidecar may echo an EVM
        // address in a different letter case than the one stored/configured.
        // TRON base58 still compares exactly.
        var snapshotByAddress = snapshot.Balances
            .GroupBy(b => b.Address, BlockchainAddress.Comparer)
            .ToDictionary(g => g.Key, g => g.First().Tokens, BlockchainAddress.Comparer);

        var detectedAt = _clock.GetUtcNow().UtcDateTime;

        var depositMismatches = 0;
        foreach (var deposit in depositAddresses)
        {
            if (!snapshotByAddress.TryGetValue(deposit.Address, out var tokens))
            {
                _logger.LogWarning(
                    "Reconciliation snapshot missing deposit address {Address}.", deposit.Address);
                continue;
            }
            depositMismatches += await ReconcileDepositAddressAsync(
                deposit.Id, deposit.Address, tokens,
                snapshot.BlockNumber, detectedAt, cancellationToken);
        }

        var hotChecked = false;
        var hotMismatches = 0;
        if (!string.IsNullOrEmpty(hotWallet)
            && snapshotByAddress.TryGetValue(hotWallet, out var hotTokens))
        {
            hotMismatches = await ReconcileHotWalletAsync(
                hotWallet, hotTokens, snapshot.BlockNumber, detectedAt, cancellationToken);
            hotChecked = true;
        }

        var coldChecked = false;
        var coldMismatches = 0;
        if (!string.IsNullOrEmpty(coldWallet)
            && snapshotByAddress.TryGetValue(coldWallet, out var coldTokens))
        {
            coldMismatches = await ReconcileColdWalletAsync(
                coldWallet, coldTokens, snapshot.BlockNumber, detectedAt, cancellationToken);
            coldChecked = true;
        }

        var totalMismatches = depositMismatches + hotMismatches + coldMismatches;
        await ReportMismatchesAsync(
            network,
            depositMismatches,
            hotChecked ? hotMismatches : null,
            coldChecked ? coldMismatches : null,
            detectedAt,
            cancellationToken);

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

    /// <summary>
    /// T158 (i) — the balance snapshot in chunks of the network's
    /// <see cref="BlockchainNetworkPolicy.BalanceSnapshotChunkSize"/> (Ethereum
    /// 10; TRON one request). The Ethereum service paces its reads through a
    /// 200 CU/s pacer it shares with the payment scanner and its hourly
    /// reconciliation, interleaved per call: an address is 3 calls (72 CU),
    /// each waiting at most one scanner <c>eth_getLogs</c> (60 CU) and one
    /// reconciliation call (≤ 26 CU), so 3 × (60 + 26) + 72 = 330 CU = 1.65 s
    /// worst case per address — so 1 request for every deposit cannot finish
    /// in any fixed budget. A 10-address chunk takes up to ≈ 16.5 s (12.5 s
    /// measured with the scanner only), and each request runs under the 30-s
    /// chain-read budget, not the 10-s wallet one
    /// (<see cref="HttpBlockchainSidecarClient.GetWalletBalancesAsync"/>); the
    /// T158 chunk of 20 (≈ 33 s worst case) ran on the 10-s client (T158
    /// doğrulaması, bulgu (i)). Any failed chunk fails the round — a partial
    /// snapshot would leave the missing addresses unchecked without saying so.
    /// The block reported is
    /// the lowest chunk's (each chunk is read at its own finalized head).
    /// </summary>
    private async Task<BlockchainSidecarBalancesResult> ReadBalancesAsync(
        BlockchainNetwork network, IReadOnlyList<string> addresses, CancellationToken cancellationToken)
    {
        var chunkSize = BlockchainNetworkPolicy.For(network).BalanceSnapshotChunkSize;
        var sidecar = _services.Sidecar(network);
        if (chunkSize is not { } size || addresses.Count <= size)
        {
            return await sidecar.GetWalletBalancesAsync(addresses, cancellationToken);
        }

        var balances = new List<BlockchainSidecarAddressBalances>(addresses.Count);
        long? block = null;
        foreach (var chunk in addresses.Chunk(size))
        {
            var part = await sidecar.GetWalletBalancesAsync(chunk, cancellationToken);
            if (part.Status != BlockchainSidecarStatus.Success || part.Balances is null)
            {
                _logger.LogWarning(
                    "Reconciliation of {Network}: a balance chunk of {Count} address(es) returned {Status} — the round is abandoned.",
                    network, chunk.Length, part.Status);
                return new BlockchainSidecarBalancesResult(part.Status, null, null);
            }
            balances.AddRange(part.Balances);
            if (part.BlockNumber is { } number && (block is null || number < block)) block = number;
        }
        return new BlockchainSidecarBalancesResult(BlockchainSidecarStatus.Success, block, balances);
    }

    /// <summary>
    /// T165 — the round's mismatches leave the panel (05 §3.3 "admin'e critical
    /// alert"). Until T165 a mismatch was an AuditLog row, a warning log line and
    /// a SignalR banner for whoever had the admin panel open — nothing reached
    /// Telegram or e-mail. Two independent paths now:
    /// <list type="number">
    ///   <item>the metric (<see cref="IReconciliationRunObserver.ReportMismatches"/>)
    ///   → the <c>reconciliation-mismatch</c> money alarm to the operator's
    ///   Telegram chat and e-mail; "resolved" when a later round of the scope
    ///   is balanced. Reported first: it needs no database.</item>
    ///   <item>one <see cref="ReconciliationMismatchAdminAlertEvent"/> per network
    ///   round with mismatches → <c>ADMIN_RECONCILIATION_MISMATCH</c> in every
    ///   admin's inbox and on each admin's connected channels. Daily while the
    ///   books stay apart, which doubles as the reminder.</item>
    /// </list>
    /// A scope the round did not compare (null) reports nothing; see
    /// <see cref="IReconciliationRunObserver.ReportMismatches"/>.
    /// </summary>
    private async Task ReportMismatchesAsync(
        BlockchainNetwork network,
        int depositMismatches,
        int? hotMismatches,
        int? coldMismatches,
        DateTime detectedAt,
        CancellationToken cancellationToken)
    {
        _runObserver.ReportMismatches(network, ReconciliationScope.DepositAddress, depositMismatches);
        if (hotMismatches is { } hot)
        {
            _runObserver.ReportMismatches(network, ReconciliationScope.HotWallet, hot);
        }
        if (coldMismatches is { } cold)
        {
            _runObserver.ReportMismatches(network, ReconciliationScope.ColdWallet, cold);
        }

        var total = depositMismatches + (hotMismatches ?? 0) + (coldMismatches ?? 0);
        if (total == 0) return;

        await _outbox.PublishAsync(
            new ReconciliationMismatchAdminAlertEvent(
                EventId: Guid.NewGuid(),
                Network: network.ToString(),
                MismatchCount: total,
                DepositAddressMismatches: depositMismatches,
                HotWalletMismatches: hotMismatches ?? 0,
                ColdWalletMismatches: coldMismatches ?? 0,
                OccurredAt: detectedAt),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string HotWalletKeyFor(BlockchainNetwork network) =>
        network == BlockchainNetwork.TRON
            ? EnvPlatformWalletAddressProvider.HotWalletConfigurationKey
            : EnvPlatformWalletAddressProvider.EthereumHotWalletConfigurationKey;

    private static string ColdWalletKeyFor(BlockchainNetwork network) =>
        network == BlockchainNetwork.TRON
            ? EnvPlatformWalletAddressProvider.ColdWalletConfigurationKey
            : EnvPlatformWalletAddressProvider.EthereumColdWalletConfigurationKey;

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
                         && ((bx.ToAddress == hotWallet && HotWalletInflowTypes.Contains(bx.Type))
                             || (bx.FromAddress == hotWallet
                                 && HotWalletOutboundTypes.Contains(bx.Type))))
            .GroupBy(bx => new { bx.Type, bx.Token })
            .Select(g => new LedgerSummaryRow(
                g.Key.Type, g.Key.Token, g.Sum(x => x.Amount)))
            .ToListAsync(cancellationToken);

        var expectedByToken = ComputeSignedExpected(ledgerRows,
            inflowTypes: HotWalletInflowTypes,
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
