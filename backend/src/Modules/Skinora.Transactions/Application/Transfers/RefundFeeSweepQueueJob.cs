using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Application.Wallets;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// T160c — collects the refund network fees withheld from buyers that the
/// transaction's own <c>SWEEP</c> could not carry (owner decision
/// 2026-10-21, F8 gate: "the remainder is collected"; backlog
/// <c>RefundGasDeductionDustStaysOnDeposit</c>). Queues a PENDING
/// <c>REFUND_FEE_SWEEP</c> row (deposit → the network's hot wallet) that
/// <see cref="OutgoingTransferDispatchJob"/> broadcasts through the sidecar's
/// <c>/api/transfer/sweep</c> and <see cref="OutgoingTransferConfirmationJob"/>
/// drives to CONFIRMED; the reconciliation counts it like a sweep.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is left for this job.</b> <see cref="SweepQueueJob"/> folds the
/// fees of refunds confirmed by then into the sale's sweep for free. Three
/// cases stay: (1) a deposit that never reaches a sweep — the transaction was
/// cancelled or refunded (pre-sweep <c>BUYER_REFUND</c>, under-payment
/// followed by timeout, late payments); (2) a fee in another token — a
/// wrong-token refund withholds the token it returned (USDC); (3) a refund
/// that became final only after the sweep was queued.
/// </para>
/// <para>
/// <b>When a deposit is looked at</b> — all of: the deposit's network has a
/// hot wallet; the transaction is not on hold and has no active dispute; the
/// sale's sweep is <c>CONFIRMED</c>, or there is no sweep and the transaction
/// is terminal without one (<see cref="TerminalWithoutSweep"/>) — a sweep
/// that is queued, in flight or FAILED leaves the deposit alone (a FAILED
/// sweep is an admin case); nothing that leaves the deposit is PENDING or
/// DETECTED (a refund on its way, a sweep or an earlier collection), so this
/// transfer never races another one for the same balance; no earlier
/// collection of the transaction FAILED (an admin looks first — the job does
/// not retry a refused transfer under a new id); the ledger still owes
/// something (<see cref="RefundFeeLedger"/>); and the deposit is due
/// (<see cref="PaymentAddress.RefundFeeSweepNextCheckAt"/>).
/// </para>
/// <para>
/// <b>Never at a loss (öneriyle kaydedildi — ⚠ PARA).</b> Collecting is a
/// transfer of its own and the platform pays its chain fee; one refund's fee
/// is about the price of one such transfer. The cost is priced live
/// (<see cref="IGasFeeEstimator.EstimateFeeUsdtAsync"/>, deposit → hot
/// wallet, this amount and token — TRON: the burn for the transfer's Energy
/// and Bandwidth, Ethereum: fuel at the upper price + the token transfer, an
/// over-estimate) and the transfer is queued only when the amount is
/// <b>greater</b> than that cost — the platform ends up with more in the hot
/// wallet than it spends. Otherwise the fee stays on the deposit, counted as
/// uncollected in the books, and is priced again after
/// <see cref="UneconomicRecheck"/>; an unavailable or implausible estimate
/// (negative, or above <c>max_charged_gas_fee_usdt</c>) is retried after
/// <see cref="UnpricedRecheck"/>. Ethereum's signer adds a second limit: the
/// dispatcher sends the row with <c>feeCapUsd = min(cap, amount)</c>, so the
/// transfer is never signed for more than it collects (it waits instead).
/// </para>
/// <para>
/// One token per deposit per run (the larger amount first); the next one goes
/// once this one is final. Idempotency: every non-FAILED collection counts as
/// carried, ticks are serialised (<see cref="DisableConcurrentExecutionAttribute"/>)
/// and <c>UQ_BlockchainTransactions_RefundFeeSweep_InFlight</c> refuses a
/// second in-flight row per transaction.
/// </para>
/// </remarks>
public sealed class RefundFeeSweepQueueJob
{
    public const string RecurringJobId = "refund-fee-sweep-queue";

    /// <summary>Every 10 minutes — nothing here is time-critical.</summary>
    public const string Cron = "*/10 * * * *";

    public const int BatchSize = 20;

    /// <summary>Lock acquisition timeout; a contending tick gives up long before the next one.</summary>
    public const int ConcurrencyLockTimeoutSeconds = 50;

    /// <summary>A deposit whose fees are not worth moving now is priced again after this.</summary>
    public static readonly TimeSpan UneconomicRecheck = TimeSpan.FromHours(24);

    /// <summary>A deposit whose collection could not be priced is asked again after this.</summary>
    public static readonly TimeSpan UnpricedRecheck = TimeSpan.FromMinutes(30);

    /// <summary>States that end a transaction without a sweep (its deposit is never swept).</summary>
    public static readonly TransactionStatus[] TerminalWithoutSweep =
    [
        TransactionStatus.CANCELLED_TIMEOUT,
        TransactionStatus.CANCELLED_SELLER,
        TransactionStatus.CANCELLED_BUYER,
        TransactionStatus.CANCELLED_ADMIN,
        TransactionStatus.REFUNDED,
    ];

    /// <summary>Rows that move money out of the deposit (and so must not be in flight).</summary>
    private static readonly BlockchainTransactionType[] DepositSendTypes =
    [
        .. RefundFeeLedger.RefundTypes,
        .. RefundFeeLedger.CollectionTypes,
    ];

    private readonly AppDbContext _db;
    private readonly IPlatformWalletAddressProvider _walletAddresses;
    private readonly IBlockchainServiceRegistry _services;
    private readonly IBlockchainNetworkSettingsProvider _networkSettings;
    private readonly IStablecoinContractRegistry _contracts;
    private readonly TimeProvider _clock;
    private readonly ILogger<RefundFeeSweepQueueJob> _logger;

    public RefundFeeSweepQueueJob(
        AppDbContext db,
        IPlatformWalletAddressProvider walletAddresses,
        IBlockchainServiceRegistry services,
        IBlockchainNetworkSettingsProvider networkSettings,
        IStablecoinContractRegistry contracts,
        TimeProvider clock,
        ILogger<RefundFeeSweepQueueJob> logger)
    {
        _db = db;
        _walletAddresses = walletAddresses;
        _services = services;
        _networkSettings = networkSettings;
        _contracts = contracts;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        // Same rule as SweepQueueJob (T148 F1): only a network with a hot
        // wallet can be collected into, and filtering in the query keeps the
        // oldest-first batch for deposits that can move.
        var collectable = Enum.GetValues<BlockchainNetwork>()
            .Where(n => _walletAddresses.For(n).HotWalletAddress is not null)
            .ToList();
        if (collectable.Count == 0) return;

        var candidateIds = await Candidates(collectable, now)
            .OrderBy(t => t.PaymentAddress!.RefundFeeSweepNextCheckAt)
            .ThenBy(t => t.CreatedAt)
            .Take(BatchSize)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        if (candidateIds.Count == 0) return;

        _logger.LogInformation(
            "RefundFeeSweepQueueJob picked up {Count} deposit(s) holding withheld refund fees", candidateIds.Count);

        foreach (var id in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CollectAsync(id, collectable, now, cancellationToken);
        }
    }

    /// <summary>The eligibility rule (class remarks) as one query.</summary>
    private IQueryable<Transaction> Candidates(List<BlockchainNetwork> networks, DateTime now) =>
        _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.PaymentAddress != null
                && networks.Contains(t.PaymentAddress.Network)
                && (t.PaymentAddress.RefundFeeSweepNextCheckAt == null
                    || t.PaymentAddress.RefundFeeSweepNextCheckAt <= now)
                && !t.IsOnHold
                && !t.HasActiveDispute
                // T160c düzeltme turu (F6) — the narrow test first: only a
                // transaction with a confirmed, fee-withholding refund from its
                // deposit can owe anything (most sales never refund). Without it
                // every completed sale with a confirmed sweep reached the two
                // sums below on every 10-minute run; the result is the same.
                && t.BlockchainTransactions.Any(b => RefundFeeLedger.RefundTypes.Contains(b.Type)
                    && b.Status == BlockchainTransactionStatus.CONFIRMED
                    && b.GasFee > 0m
                    && b.FromAddress == t.PaymentAddress.Address)
                && (t.BlockchainTransactions.Any(b => b.Type == BlockchainTransactionType.SWEEP
                        && b.Status == BlockchainTransactionStatus.CONFIRMED)
                    || (TerminalWithoutSweep.Contains(t.Status)
                        && !t.BlockchainTransactions.Any(b => b.Type == BlockchainTransactionType.SWEEP)))
                && !t.BlockchainTransactions.Any(b => DepositSendTypes.Contains(b.Type)
                    && (b.Status == BlockchainTransactionStatus.PENDING
                        || b.Status == BlockchainTransactionStatus.DETECTED))
                && !t.BlockchainTransactions.Any(b => b.Type == BlockchainTransactionType.REFUND_FEE_SWEEP
                    && b.Status == BlockchainTransactionStatus.FAILED)
                // Coarse "still owes something" over all tokens: carried never
                // exceeds withheld per token, so the totals differ exactly when
                // some token has a remainder (exact split in CollectAsync).
                && t.BlockchainTransactions
                    .Where(b => RefundFeeLedger.RefundTypes.Contains(b.Type)
                        && b.Status == BlockchainTransactionStatus.CONFIRMED
                        && b.GasFee > 0m
                        && b.FromAddress == t.PaymentAddress.Address)
                    .Sum(b => b.GasFee ?? 0m)
                    > t.BlockchainTransactions
                    .Where(b => RefundFeeLedger.CollectionTypes.Contains(b.Type)
                        && b.Status != BlockchainTransactionStatus.FAILED)
                    .Sum(b => b.RefundFeeRecovered ?? 0m));

    private async Task CollectAsync(
        Guid transactionId, List<BlockchainNetwork> collectable, DateTime now, CancellationToken cancellationToken)
    {
        // Re-validate inside the loop (09 §13.3): a refund, sweep or hold may
        // have started since the batch was read.
        var stillEligible = await Candidates(collectable, now)
            .AnyAsync(t => t.Id == transactionId, cancellationToken);
        if (!stillEligible) return;

        var deposit = await _db.Set<PaymentAddress>()
            .FirstOrDefaultAsync(p => p.TransactionId == transactionId, cancellationToken);
        if (deposit is null) return;

        var hotWallet = _walletAddresses.For(deposit.Network).HotWalletAddress;
        if (hotWallet is null) return;

        var rows = await RefundFeeLedger.LoadAsync(_db, transactionId, cancellationToken);
        var owed = RefundFeeLedger.Uncollected(
            rows, deposit.Address, r => OutboundTransferExpectation.ChainToken(r, _contracts));
        if (owed.Count == 0)
        {
            // The coarse query saw a remainder this rule does not (a wrong-token
            // refund whose contract no longer resolves): nothing to move.
            await DeferAsync(deposit, now + UneconomicRecheck, cancellationToken);
            return;
        }

        var (token, amount) = owed
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv => (kv.Key, kv.Value))
            .First();

        var settings = await _networkSettings.GetAsync(deposit.Network, cancellationToken);
        var cost = await PriceAsync(deposit, hotWallet, amount, token, settings, cancellationToken);
        if (cost is null)
        {
            await DeferAsync(deposit, now + UnpricedRecheck, cancellationToken);
            return;
        }

        if (amount <= cost.Value)
        {
            _logger.LogInformation(
                "Refund fees on {Network} deposit {Deposit} (transaction {TransactionId}): {Amount} {Token} uncollected — collecting would cost ~{Cost} USDT, not less than it brings; kept on the deposit, priced again after {Recheck}.",
                deposit.Network, deposit.Address, transactionId, amount, token, cost.Value, UneconomicRecheck);
            await DeferAsync(deposit, now + UneconomicRecheck, cancellationToken);
            return;
        }

        var row = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            PaymentAddressId = deposit.Id,   // CK_..._Type_RefundFeeSweep: deposit-anchored.
            Network = deposit.Network,       // Same-network rule (11 §F8 K3).
            Type = BlockchainTransactionType.REFUND_FEE_SWEEP,
            TxHash = null,
            FromAddress = deposit.Address,
            ToAddress = hotWallet,
            Amount = amount,
            Token = token,                   // The token actually moved (USDC for a wrong-token refund's fee).
            ActualTokenAddress = null,
            GasFee = null,                   // Platform-paid; the realized fee is read at confirmation.
            RefundFeeRecovered = amount,     // Every unit moved is a collected refund fee.
            Status = BlockchainTransactionStatus.PENDING,
            ConfirmationCount = 0,
            RetryCount = 0,
            NextAttemptAt = null,
            CreatedAt = now,
        };
        _db.Set<BlockchainTransaction>().Add(row);
        deposit.RefundFeeSweepNextCheckAt = null;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Backstop: a concurrent tick inserted the in-flight row first and
            // the filtered unique index refused this one. Anything else re-throws.
            _db.Entry(row).State = EntityState.Detached;
            _db.Entry(deposit).State = EntityState.Unchanged;
            var inFlight = await _db.Set<BlockchainTransaction>()
                .AsNoTracking()
                .AnyAsync(b => b.TransactionId == transactionId
                    && b.Type == BlockchainTransactionType.REFUND_FEE_SWEEP
                    && (b.Status == BlockchainTransactionStatus.PENDING
                        || b.Status == BlockchainTransactionStatus.DETECTED),
                    cancellationToken);
            if (!inFlight) throw;
            _logger.LogWarning(
                "Refund-fee collection for transaction {TransactionId} ({Token}) — another collection was queued by a concurrent tick; skipping (idempotent).",
                transactionId, token);
            return;
        }

        _logger.LogInformation(
            "Refund fees queued for collection — transaction {TransactionId} row {RowId}: {Amount} {Token} from {Network} deposit {Deposit} → hot wallet (cost ~{Cost} USDT).",
            transactionId, row.Id, amount, token, deposit.Network, deposit.Address, cost.Value);
    }

    /// <summary>
    /// The live price of moving <paramref name="amount"/> from the deposit to
    /// the hot wallet, USDT; <c>null</c> when it cannot be trusted (no answer,
    /// negative, or above the network's sanity ceiling — refused, not clamped,
    /// the same rule the charged fee follows).
    /// </summary>
    private async Task<decimal?> PriceAsync(
        PaymentAddress deposit,
        string hotWallet,
        decimal amount,
        StablecoinType token,
        BlockchainNetworkSettings settings,
        CancellationToken cancellationToken)
    {
        decimal? estimate;
        try
        {
            estimate = await _services.GasFee(deposit.Network).EstimateFeeUsdtAsync(
                new GasFeeEstimateRequest(deposit.Address, hotWallet, amount, token), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Refund-fee collection on {Network} deposit {Deposit}: the cost estimate failed — asked again after {Recheck}.",
                deposit.Network, deposit.Address, UnpricedRecheck);
            return null;
        }

        if (estimate is not { } fee)
        {
            _logger.LogWarning(
                "Refund-fee collection on {Network} deposit {Deposit}: no cost estimate — nothing queued, asked again after {Recheck}.",
                deposit.Network, deposit.Address, UnpricedRecheck);
            return null;
        }
        if (fee < 0m || (settings.MaxChargedGasFeeUsdt > 0m && fee > settings.MaxChargedGasFeeUsdt))
        {
            _logger.LogError(
                "Refund-fee collection on {Network} deposit {Deposit}: cost estimate {Fee} USDT is implausible (ceiling {Cap}) — not believed, nothing queued, asked again after {Recheck}.",
                deposit.Network, deposit.Address, fee, settings.MaxChargedGasFeeUsdt, UnpricedRecheck);
            return null;
        }
        return fee;
    }

    private async Task DeferAsync(PaymentAddress deposit, DateTime nextCheckAt, CancellationToken cancellationToken)
    {
        deposit.RefundFeeSweepNextCheckAt = nextCheckAt;
        await _db.SaveChangesAsync(cancellationToken);
    }

    // Hangfire serializes Expression<Action<T>>; expose a sync wrapper.
    [DisableConcurrentExecution(ConcurrencyLockTimeoutSeconds)]
    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
