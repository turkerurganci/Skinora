using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Tests.Helpers;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Transfers;

/// <summary>
/// Unit coverage for <see cref="PaymentRefundToBuyerConsumer"/> (WP2 — 02 §4.6,
/// 03 §4.4). Confirms the net buyer refund (TotalAmount − gas) is queued as a
/// PENDING BUYER_REFUND row with the gas estimate snapshotted, that a blocked
/// decision raises an admin alert and queues no row, redelivery idempotency,
/// the F1 concurrent-insert race backstop, and the missing-transaction no-op.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PaymentRefundToBuyerConsumerTests : IDisposable
{
    private const string BuyerRefundAddress = "TBuyerRefund000000000000000000000000";

    static PaymentRefundToBuyerConsumerTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly AppDbContext _db;
    private readonly StubGasFeeSettingsProvider _settings;
    private readonly StubChargedGasFeeResolver _gasFee;
    private readonly RecordingRefundBlockedAlertService _alert;
    private readonly FakeTimeProvider _clock;
    private readonly PaymentRefundToBuyerConsumer _sut;

    public PaymentRefundToBuyerConsumerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(_options);
        _db.Database.EnsureCreated();

        _settings = new StubGasFeeSettingsProvider
        {
            Settings = new GasFeeSettings(
                ProtectionRatio: 0.10m,
                MinRefundThresholdRatio: 2m,
                RefundGasFeeEstimateUsdt: 2m,
                PayoutGasFeeEstimateUsdt: 0.50m, MaxChargedGasFeeUsdt: 10m),
        };
        _gasFee = new StubChargedGasFeeResolver { RefundFee = 2m };
        _alert = new RecordingRefundBlockedAlertService();
        _clock = new FakeTimeProvider();
        _clock.SetUtcNow(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        _sut = NewConsumer(_db, NullLogger<PaymentRefundToBuyerConsumer>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private PaymentRefundToBuyerConsumer NewConsumer(
        AppDbContext db, ILogger<PaymentRefundToBuyerConsumer> logger) =>
        new(db, _gasFee, GasFeeSettingsBackedNetworkSettings.NewRefundDecisionService(_settings), _alert, _clock, logger);

    [Fact]
    public async Task ValidRefund_QueuesPendingBuyerRefund_WithNetAmountAndGasSnapshot()
    {
        // TotalAmount 102, gasFee 2 → net 100 (02 §4.6 Price+Commission−gas).
        var tx = await SeedCancelledAsync(price: 100m, commission: 2m);

        await _sut.Handle(EventFor(tx), CancellationToken.None);

        var refund = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.BUYER_REFUND);
        Assert.Equal(BlockchainTransactionStatus.PENDING, refund.Status);
        Assert.Equal(100m, refund.Amount);
        Assert.Equal(2m, refund.GasFee);
        Assert.Equal(BuyerRefundAddress, refund.ToAddress);
        Assert.Equal(StablecoinType.USDT, refund.Token);
        Assert.Null(refund.PaymentAddressId);
        Assert.Null(refund.ActualTokenAddress);
        Assert.Equal(string.Empty, refund.FromAddress);
        Assert.Null(refund.NextAttemptAt);
        Assert.Empty(_alert.Raised);
    }

    [Fact]
    public async Task RuntimeEstimate_IsChargedAndSnapshotted()
    {
        // Prova-GasFeeChargedIsFixedGuess: the net amount and the GasFee
        // snapshot must carry the RESOLVED runtime value, not the static 2.00
        // setting. TotalAmount 102, fee 0.18 → net 101.82.
        _gasFee.RefundFee = 0.18m;
        var tx = await SeedCancelledAsync(price: 100m, commission: 2m);

        await _sut.Handle(EventFor(tx), CancellationToken.None);

        var refund = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.BUYER_REFUND);
        Assert.Equal(101.82m, refund.Amount);
        Assert.Equal(0.18m, refund.GasFee);
        var call = Assert.Single(_gasFee.RefundCalls);
        Assert.Equal(BuyerRefundAddress, call.To);
        Assert.Equal(102m, call.Amount);
        Assert.Equal(StablecoinType.USDT, call.Token);
        // No PaymentAddress row seeded → the consumer passes null; what a
        // missing sender costs is the resolver's decision (static fallback —
        // ChargedGasFeeResolverTests).
        Assert.Null(call.From);
    }

    [Fact]
    public async Task DepositAddress_IsTheRefundEstimateSender()
    {
        // The refund is broadcast FROM the transaction's deposit, so the
        // deposit is the sender the estimate must price. A broken lookup would
        // not fail loudly: the resolver charges the static refund setting for
        // a sender-less refund, so every cancellation refund would silently
        // pay 2.00 instead of the runtime estimate (#327 validation — passing
        // null here left every test green).
        var tx = await SeedCancelledAsync(price: 100m, commission: 2m);
        const string depositAddress = "TDepositRefund0000000000000000000000";
        _db.Set<PaymentAddress>().Add(new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Address = depositAddress,
            HdWalletIndex = 7,
            ExpectedAmount = tx.TotalAmount,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync();

        await _sut.Handle(EventFor(tx), CancellationToken.None);

        var call = Assert.Single(_gasFee.RefundCalls);
        Assert.Equal(depositAddress, call.From);
        Assert.Equal(BuyerRefundAddress, call.To);
    }

    [Fact]
    public async Task Redelivery_QueuesExactlyOneRow()
    {
        // At-least-once outbox redelivery: the AnyAsync guard makes the second
        // delivery a no-op, and the filtered unique index holds the invariant
        // even if that guard were ever bypassed.
        var tx = await SeedCancelledAsync(price: 100m, commission: 2m);

        await _sut.Handle(EventFor(tx), CancellationToken.None);
        await _sut.Handle(EventFor(tx), CancellationToken.None);

        var count = await _db.Set<BlockchainTransaction>().CountAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.BUYER_REFUND);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task BelowThresholdRefund_RaisesAdminAlert_AndQueuesNoRow()
    {
        // gasFee 51 → net 51, threshold 51×2=102 → net < threshold → Block.
        _gasFee.RefundFee = 51m;
        var tx = await SeedCancelledAsync(price: 100m, commission: 2m);

        await _sut.Handle(EventFor(tx), CancellationToken.None);

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.BUYER_REFUND));
        var raised = Assert.Single(_alert.Raised);
        Assert.Equal(tx.Id, raised.TransactionId);
        Assert.Equal(RefundOutcome.Block, raised.Decision.Outcome);
        Assert.Equal(RefundBlockedReason.BelowMinimumThreshold, raised.Decision.Reason);
    }

    [Fact]
    public async Task NegativeRefund_RaisesAdminAlert_AndQueuesNoRow()
    {
        // gasFee 200 > TotalAmount 102 → net negative → Block (NegativeAmount).
        _gasFee.RefundFee = 200m;
        var tx = await SeedCancelledAsync(price: 100m, commission: 2m);

        await _sut.Handle(EventFor(tx), CancellationToken.None);

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.BUYER_REFUND));
        var raised = Assert.Single(_alert.Raised);
        Assert.Equal(RefundBlockedReason.NegativeAmount, raised.Decision.Reason);
    }

    [Fact]
    public async Task MissingTransaction_IsNoOp()
    {
        // No throw, no row, no alert — a refund event for an unknown
        // transaction is logged and dropped.
        await _sut.Handle(
            new PaymentRefundToBuyerRequestedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: Guid.NewGuid(),
                BuyerId: Guid.NewGuid(),
                BuyerRefundAddress: BuyerRefundAddress,
                OccurredAt: _clock.GetUtcNow().UtcDateTime),
            CancellationToken.None);

        Assert.Equal(0, await _db.Set<BlockchainTransaction>().CountAsync());
        Assert.Empty(_alert.Raised);
    }

    [Fact]
    public async Task ConcurrentInsertRace_SwallowsDuplicate_AndDoesNotDoubleRefund()
    {
        // Drives the consumer's catch(DbUpdateException) backstop (WP1 F1). A
        // competing delivery commits the BUYER_REFUND row in the window between
        // this delivery's AnyAsync guard and its SaveChanges, so the filtered
        // unique index rejects this insert. The catch must detach, re-query,
        // confirm the row now exists, and swallow as an idempotent no-op —
        // exactly one row, no escaping exception.
        var tx = await SeedCancelledAsync(price: 100m, commission: 2m);
        var logger = new ListLogger<PaymentRefundToBuyerConsumer>();

        await using var raceDb = new RaceDbContext(_options, injectBeforeSave: async () =>
        {
            await using var competing = new AppDbContext(_options);
            competing.Set<BlockchainTransaction>().Add(NewBuyerRefundRow(tx));
            await competing.SaveChangesAsync();
        });
        var sut = NewConsumer(raceDb, logger);

        await sut.Handle(EventFor(tx), CancellationToken.None);   // must not throw

        var count = await _db.Set<BlockchainTransaction>().CountAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.BUYER_REFUND);
        Assert.Equal(1, count);
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("concurrent insert race"));
    }

    [Fact]
    public async Task NonDuplicateDbUpdateException_IsRethrown_NotMasked()
    {
        // The catch must only swallow when a BUYER_REFUND row genuinely now
        // exists. An unrelated DbUpdateException (no row created) must surface
        // unchanged — never be masked as an idempotent no-op.
        var tx = await SeedCancelledAsync(price: 100m, commission: 2m);
        await using var throwingDb = new RaceDbContext(_options, throwUnrelated: true);
        var sut = NewConsumer(throwingDb, NullLogger<PaymentRefundToBuyerConsumer>.Instance);

        await Assert.ThrowsAsync<DbUpdateException>(() => sut.Handle(EventFor(tx), CancellationToken.None));

        Assert.Equal(0, await _db.Set<BlockchainTransaction>().CountAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.BUYER_REFUND));
    }

    // ─── T149: the TRON network fee line (11 §F8 K8, 02 §4.6, 08 §3.1a) ───

    /// <summary>02 §5 example: 20 + 0.40 + 2.90 = 23.30 on TRON.</summary>
    private Task<Transaction> SeedTronWithFeeLineAsync() =>
        SeedCancelledAsync(price: 20m, commission: 0.40m, configure: t =>
        {
            t.Network = BlockchainNetwork.TRON;
            t.NetworkFeeAmount = 2.90m;
            t.TotalAmount = 23.30m;
        });

    private async Task AddSweepAsync(Transaction tx, BlockchainTransactionStatus status, bool broadcast)
    {
        var deposit = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Address = "TDepositSwept00000000000000000000000",
            HdWalletIndex = 11,
            ExpectedAmount = tx.TotalAmount,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<PaymentAddress>().Add(deposit);
        _db.Set<BlockchainTransaction>().Add(new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = deposit.Id,
            Type = BlockchainTransactionType.SWEEP,
            TxHash = broadcast ? "sweep" + Guid.NewGuid().ToString("N") : null,
            FromAddress = deposit.Address,
            ToAddress = "THotWallet000000000000000000000000",
            Amount = tx.TotalAmount,
            Token = StablecoinType.USDT,
            Status = status,
            BlockNumber = status == BlockchainTransactionStatus.CONFIRMED ? 1_000L : null,
            ConfirmationCount = status == BlockchainTransactionStatus.CONFIRMED ? 20 : 0,
            ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED ? _clock.GetUtcNow().UtcDateTime : null,
            RetryCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync();
    }

    private async Task<BlockchainTransaction> RefundRowAsync(Transaction tx) =>
        await _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == tx.Id && b.Type == BlockchainTransactionType.BUYER_REFUND);

    [Fact]
    public async Task FeeLine_RefundBeforeSweep_ReturnsTheLineInFull()
    {
        // No sweep has run: 23.30 − 2.00 gas = 21.30 (price, commission and the
        // 2.90 line all come back, like the commission — K8).
        var tx = await SeedTronWithFeeLineAsync();

        await _sut.Handle(EventFor(tx), CancellationToken.None);

        var refund = await RefundRowAsync(tx);
        Assert.Equal(21.30m, refund.Amount);
        Assert.Equal(23.30m, Assert.Single(_gasFee.RefundCalls).Amount);
    }

    [Fact]
    public async Task FeeLine_SweepQueuedButNotBroadcast_StillReturnsTheLine()
    {
        var tx = await SeedTronWithFeeLineAsync();
        await AddSweepAsync(tx, BlockchainTransactionStatus.PENDING, broadcast: false);

        await _sut.Handle(EventFor(tx), CancellationToken.None);

        Assert.Equal(21.30m, (await RefundRowAsync(tx)).Amount);
    }

    [Fact]
    public async Task FeeLine_FailedSweep_StillReturnsTheLine()
    {
        // A sweep that failed left the money in the deposit — it did not happen.
        var tx = await SeedTronWithFeeLineAsync();
        await AddSweepAsync(tx, BlockchainTransactionStatus.FAILED, broadcast: true);

        await _sut.Handle(EventFor(tx), CancellationToken.None);

        Assert.Equal(21.30m, (await RefundRowAsync(tx)).Amount);
    }

    [Theory]
    [InlineData(BlockchainTransactionStatus.CONFIRMED)]
    [InlineData(BlockchainTransactionStatus.PENDING)]   // broadcast, awaiting confirmation
    [InlineData(BlockchainTransactionStatus.DETECTED)]  // broadcast and seen on chain, below the confirmation depth (T158 closing: money — the line is already spent)
    public async Task FeeLine_RefundAfterSweep_KeepsTheLine(BlockchainTransactionStatus sweepStatus)
    {
        // The sweep ran, so the line was spent on what it paid for: the refund
        // returns price + commission (20.40) − 2.00 gas = 18.40.
        var tx = await SeedTronWithFeeLineAsync();
        await AddSweepAsync(tx, sweepStatus, broadcast: true);

        await _sut.Handle(EventFor(tx), CancellationToken.None);

        var refund = await RefundRowAsync(tx);
        Assert.Equal(18.40m, refund.Amount);
        Assert.Equal(20.40m, Assert.Single(_gasFee.RefundCalls).Amount);
    }

    [Fact]
    public async Task FeeLine_Threshold_Uses_The_Refundable_Base()
    {
        // gas 6.80: before the sweep 23.30 − 6.80 = 16.50 ≥ 13.60 → refunded;
        // after it 20.40 − 6.80 = 13.60 ≥ 13.60 → still refunded (boundary, ≥).
        _gasFee.RefundFee = 6.80m;
        var tx = await SeedTronWithFeeLineAsync();
        await AddSweepAsync(tx, BlockchainTransactionStatus.CONFIRMED, broadcast: true);

        await _sut.Handle(EventFor(tx), CancellationToken.None);

        Assert.Equal(13.60m, (await RefundRowAsync(tx)).Amount);
        Assert.Empty(_alert.Raised);
    }

    // ---- T158 (f) — an Ethereum refund after the sweep is priced from the hot wallet ----

    private async Task<Transaction> SeedEthereumWithSweepAsync(BlockchainTransactionStatus? sweepStatus)
    {
        var tx = await SeedCancelledAsync(price: 20m, commission: 0.40m, t => t.Network = BlockchainNetwork.ETHEREUM);
        var deposit = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Network = BlockchainNetwork.ETHEREUM,
            Address = "0x9858effd232b4033e47d90003d41ec34ecaeda94",
            HdWalletIndex = 21,
            ExpectedAmount = tx.TotalAmount,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<PaymentAddress>().Add(deposit);
        if (sweepStatus is { } status)
        {
            _db.Set<BlockchainTransaction>().Add(new BlockchainTransaction
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                PaymentAddressId = deposit.Id,
                Network = BlockchainNetwork.ETHEREUM,
                Type = BlockchainTransactionType.SWEEP,
                TxHash = status == BlockchainTransactionStatus.PENDING ? null : "0xsweep" + Guid.NewGuid().ToString("N"),
                FromAddress = deposit.Address,
                ToAddress = "0x7070707070707070707070707070707070707070",
                Amount = tx.TotalAmount,
                Token = StablecoinType.USDT,
                Status = status,
                BlockNumber = status == BlockchainTransactionStatus.CONFIRMED ? 26_215_000L : null,
                ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED ? _clock.GetUtcNow().UtcDateTime : null,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
            });
        }
        await _db.SaveChangesAsync();
        return tx;
    }

    [Theory]
    [InlineData(BlockchainTransactionStatus.CONFIRMED)]
    [InlineData(BlockchainTransactionStatus.DETECTED)]
    [InlineData(BlockchainTransactionStatus.PENDING)]
    public async Task T158f_Ethereum_Refund_After_Or_During_The_Sweep_Is_Priced_From_The_Hot_Wallet(
        BlockchainTransactionStatus sweepStatus)
    {
        var tx = await SeedEthereumWithSweepAsync(sweepStatus);

        await _sut.Handle(EventFor(tx), CancellationToken.None);

        Assert.Empty(_gasFee.RefundCalls);
        Assert.Equal((BuyerRefundAddress, 20.40m), Assert.Single(_gasFee.HotWalletRefundCalls));
    }

    [Theory]
    [InlineData(BlockchainTransactionStatus.FAILED)]
    [InlineData(null)]
    public async Task T158f_Ethereum_Refund_Before_Or_Without_A_Settled_Sweep_Is_Priced_From_The_Deposit(
        BlockchainTransactionStatus? sweepStatus)
    {
        var tx = await SeedEthereumWithSweepAsync(sweepStatus);

        await _sut.Handle(EventFor(tx), CancellationToken.None);

        Assert.Empty(_gasFee.HotWalletRefundCalls);
        Assert.Equal("0x9858effd232b4033e47d90003d41ec34ecaeda94", Assert.Single(_gasFee.RefundCalls).From);
    }

    private PaymentRefundToBuyerRequestedEvent EventFor(Transaction tx) =>
        new(
            EventId: Guid.NewGuid(),
            TransactionId: tx.Id,
            BuyerId: tx.BuyerId ?? Guid.NewGuid(),
            BuyerRefundAddress: BuyerRefundAddress,
            OccurredAt: _clock.GetUtcNow().UtcDateTime);

    private BlockchainTransaction NewBuyerRefundRow(Transaction tx) => new()
    {
        Id = Guid.NewGuid(),
        TransactionId = tx.Id,
        Type = BlockchainTransactionType.BUYER_REFUND,
        FromAddress = string.Empty,
        ToAddress = BuyerRefundAddress,
        Amount = 100m,
        Token = StablecoinType.USDT,
        GasFee = 2m,
        Status = BlockchainTransactionStatus.PENDING,
        ConfirmationCount = 0,
        RetryCount = 0,
        CreatedAt = _clock.GetUtcNow().UtcDateTime,
    };

    private async Task<Transaction> SeedCancelledAsync(
        decimal price, decimal commission, Action<Transaction>? configure = null)
    {
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000831",
            SteamDisplayName = "Seller",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000832",
            SteamDisplayName = "Buyer",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<User>().AddRange(seller, buyer);

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.CANCELLED_ADMIN,
            SellerId = seller.Id,
            BuyerId = buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000913",
            BuyerRefundAddress = BuyerRefundAddress,
            ItemAssetId = "asset-1",
            ItemClassId = "cls",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = price,
            CommissionRate = 0.02m,
            CommissionAmount = commission,
            TotalAmount = price + commission,
            SellerPayoutAddress = "TSellerPayout00000000000000000000000",
            // CK_Transactions_Cancel — CANCELLED_* requires these three.
            CancelledBy = CancelledByType.ADMIN,
            CancelReason = "admin cancel (test)",
            CancelledAt = _clock.GetUtcNow().UtcDateTime,
        };
        configure?.Invoke(tx);

        _db.Set<Transaction>().Add(tx);
        await _db.SaveChangesAsync();
        return tx;
    }

    private sealed class StubGasFeeSettingsProvider : IGasFeeSettingsProvider
    {
        public GasFeeSettings Settings { get; set; } =
            new(0.10m, 2m, 2m, 0.50m, 10m);

        public Task<GasFeeSettings> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Settings);
    }

    private sealed class StubChargedGasFeeResolver : IChargedGasFeeResolver
    {
        public decimal RefundFee { get; set; } = 2m;
        public decimal PayoutFee { get; set; } = 0.50m;
        public GasFeeSource Source { get; set; } = GasFeeSource.RuntimeEstimate;
        public List<BlockchainNetwork> Networks { get; } = [];
        public List<(string? From, string To, decimal Amount, StablecoinType Token)> RefundCalls { get; } = [];

        public Task<ResolvedGasFee> ResolveRefundFeeAsync(
            BlockchainNetwork network, string? fromDepositAddress, string toAddress, decimal amount,
            StablecoinType token, CancellationToken cancellationToken)
        {
            Networks.Add(network);
            RefundCalls.Add((fromDepositAddress, toAddress, amount, token));
            return Task.FromResult(new ResolvedGasFee(RefundFee, Source));
        }

        /// <summary>T158 (f) — hot-wallet-sourced refunds are recorded with a null sender.</summary>
        public List<(string To, decimal Amount)> HotWalletRefundCalls { get; } = [];

        public Task<ResolvedGasFee> ResolveHotWalletRefundFeeAsync(
            BlockchainNetwork network, string toAddress, decimal amount, StablecoinType token,
            CancellationToken cancellationToken)
        {
            Networks.Add(network);
            HotWalletRefundCalls.Add((toAddress, amount));
            return Task.FromResult(new ResolvedGasFee(RefundFee, Source));
        }

        public Task<ResolvedGasFee> ResolvePayoutFeeAsync(
            BlockchainNetwork network, string toAddress, decimal amount, StablecoinType token,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ResolvedGasFee(PayoutFee, Source));
    }

    private sealed class RecordingRefundBlockedAlertService : IRefundBlockedAlertService
    {
        public List<(Guid TransactionId, RefundDecision Decision)> Raised { get; } = new();

        public Task RaiseAsync(Guid transactionId, RefundDecision decision, CancellationToken cancellationToken)
        {
            Raised.Add((transactionId, decision));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Test seam exercising <see cref="PaymentRefundToBuyerConsumer"/>'s
    /// catch(DbUpdateException) backstop (WP1 F1). On the first SaveChanges that
    /// adds a BUYER_REFUND row it either commits a competing row out-of-band (so
    /// the real filtered unique index rejects the consumer's insert → swallow
    /// branch) or throws an unrelated DbUpdateException with no row created
    /// (→ re-throw branch).
    /// </summary>
    private sealed class RaceDbContext : AppDbContext
    {
        private readonly Func<Task>? _injectBeforeSave;
        private readonly bool _throwUnrelated;
        private bool _fired;

        public RaceDbContext(
            DbContextOptions<AppDbContext> options,
            Func<Task>? injectBeforeSave = null,
            bool throwUnrelated = false)
            : base(options)
        {
            _injectBeforeSave = injectBeforeSave;
            _throwUnrelated = throwUnrelated;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var addingRefund = !_fired && ChangeTracker.Entries<BlockchainTransaction>()
                .Any(e => e.State == EntityState.Added
                    && e.Entity.Type == BlockchainTransactionType.BUYER_REFUND);
            if (addingRefund)
            {
                _fired = true;
                if (_throwUnrelated)
                {
                    throw new DbUpdateException(
                        "simulated non-duplicate failure", new InvalidOperationException());
                }
                if (_injectBeforeSave is not null)
                {
                    await _injectBeforeSave();
                }
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
