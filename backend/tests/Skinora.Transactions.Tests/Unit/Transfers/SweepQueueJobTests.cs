using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Platform.Application.Wallets;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Transfers;

/// <summary>
/// Unit coverage for <see cref="SweepQueueJob"/> (WP3 — 05 §3.3,
/// PRE_F6_PLAN WP3). Confirms the deposit → hot wallet SWEEP row is queued
/// once a transaction reaches ITEM_DELIVERED (deferred past the buyer-refund
/// window), with the deposit-anchored source + hot-wallet destination, full
/// escrow amount, and null gas; that held / disputed / non-delivered /
/// already-swept transactions and an unconfigured hot wallet are skipped; and
/// that the WP1-F1 money-safety idempotency triple (AnyAsync + filtered unique
/// index + catch) holds.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SweepQueueJobTests : IDisposable
{
    static SweepQueueJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string HotWalletAddress = "THotWalletSweepFixture0000000000000";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly AppDbContext _db;
    private readonly FakeTimeProvider _clock;
    private readonly StubWalletAddresses _walletAddresses = new();
    private readonly SweepQueueJob _sut;
    private int _seedSequence;

    public SweepQueueJobTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(_options);
        _db.Database.EnsureCreated();

        _clock = new FakeTimeProvider();
        _clock.SetUtcNow(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));

        _sut = new SweepQueueJob(_db, _walletAddresses, _clock, NullLogger<SweepQueueJob>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ConfiguredHotWallet_DeliveredTx_QueuesPendingSweep_DepositSource_HotDestination()
    {
        await ConfigureHotWalletAsync();
        var (tx, deposit) = await SeedDeliveredAsync(price: 100m, commission: 2m);

        await _sut.ExecuteAsync();

        var sweep = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SWEEP);
        Assert.Equal(BlockchainTransactionStatus.PENDING, sweep.Status);
        Assert.Equal(102m, sweep.Amount);                 // full escrow total (price + commission)
        Assert.Equal(deposit.Id, sweep.PaymentAddressId);  // deposit-anchored (CK_..._Type_Sweep)
        Assert.Equal(deposit.Address, sweep.FromAddress);  // source deposit
        Assert.Equal(HotWalletAddress, sweep.ToAddress);   // hot-wallet destination
        Assert.Equal(StablecoinType.USDT, sweep.Token);
        Assert.Null(sweep.ActualTokenAddress);             // CK_..._Type_Sweep: NULL
        Assert.Null(sweep.GasFee);                         // sweeper funds energy via delegation
        Assert.Null(sweep.RefundFeeRecovered);             // T160c — no refund fee to carry
        Assert.Null(sweep.NextAttemptAt);
    }

    // -------------------- T148 — each network sweeps into its own hot wallet --------------------

    private const string EthereumHotWallet = "0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed";

    [Fact]
    public async Task Ethereum_Deposit_Is_Never_Swept_To_Trons_Hot_Wallet()
    {
        // TRON's hot wallet is configured, Ethereum's is not: the Ethereum
        // deposit waits rather than being queued towards the wrong network's
        // wallet (11 §F8 K3).
        _walletAddresses.HotWalletAddress = HotWalletAddress;
        var (_, deposit) = await SeedDeliveredAsync(price: 100m, commission: 2m);
        deposit.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(b => b.Type == BlockchainTransactionType.SWEEP));
    }

    [Fact]
    public async Task Ethereum_Deposit_Is_Swept_To_Ethereums_Hot_Wallet_On_Ethereum()
    {
        _walletAddresses.HotWalletAddress = HotWalletAddress;
        _walletAddresses.EthereumHotWalletAddress = EthereumHotWallet;
        var (_, deposit) = await SeedDeliveredAsync(price: 100m, commission: 2m);
        deposit.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var sweep = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.Type == BlockchainTransactionType.SWEEP);
        Assert.Equal(BlockchainNetwork.ETHEREUM, sweep.Network);
        Assert.Equal(EthereumHotWallet, sweep.ToAddress);
    }

    // T148 düzeltme turu 1 (F1) — an Ethereum deposit with no Ethereum hot
    // wallet never gets a sweep row; picked oldest-first it would hold every
    // slot of the batch on every run. The newer TRON deposit must be queued on
    // the first run behind BatchSize (and more) of them.
    [Theory]
    [InlineData(SweepQueueJob.BatchSize)]
    [InlineData(SweepQueueJob.BatchSize + 5)]
    public async Task Waiting_Ethereum_Deposits_Do_Not_Starve_Tron_Sweeps(int waiting)
    {
        _walletAddresses.HotWalletAddress = HotWalletAddress;
        for (var i = 0; i < waiting; i++)
        {
            await SeedDeliveredAsync(price: 10m, commission: 1m, depositNetwork: BlockchainNetwork.ETHEREUM);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        var (tron, _) = await SeedDeliveredAsync(price: 100m, commission: 2m);

        await _sut.ExecuteAsync();

        var sweep = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.Type == BlockchainTransactionType.SWEEP);
        Assert.Equal(tron.Id, sweep.TransactionId);
        Assert.Equal(BlockchainNetwork.TRON, sweep.Network);
        Assert.Equal(HotWalletAddress, sweep.ToAddress);
    }

    [Fact]
    public async Task HotWalletUnconfigured_QueuesNoSweep()
    {
        // HOT_WALLET_ADDRESS is unset in this fixture; the job must skip the
        // whole run rather than queue a row with a bogus destination.
        var (tx, _) = await SeedDeliveredAsync(price: 100m, commission: 2m);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    [Fact]
    public async Task HeldTransaction_IsSkipped()
    {
        await ConfigureHotWalletAsync();
        var (tx, _) = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
        {
            t.IsOnHold = true;
            t.EmergencyHoldAt = _clock.GetUtcNow().UtcDateTime;
            t.EmergencyHoldReason = "test hold";
            t.EmergencyHoldByAdminId = t.SellerId; // existing user — satisfies FK.
            t.TimeoutFrozenAt = _clock.GetUtcNow().UtcDateTime;
            t.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
            t.TimeoutRemainingSeconds = 0;
        });

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    [Fact]
    public async Task DisputedTransaction_IsSkipped()
    {
        await ConfigureHotWalletAsync();
        var (tx, _) = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.HasActiveDispute = true);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    [Fact]
    public async Task NonDeliveredTransaction_IsSkipped()
    {
        await ConfigureHotWalletAsync();
        var (tx, _) = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.Status = TransactionStatus.PAYMENT_RECEIVED);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    /// <summary>
    /// T129 — the deposit is the source a buyer refund draws from (WP2), and the
    /// most likely refund left at this point is a reversal detected at the end
    /// of the settlement window. Sweeping before that check would empty the
    /// deposit shortly before the one refund it exists to fund.
    /// </summary>
    [Fact]
    public async Task UnverifiedSettlement_IsSkipped()
    {
        await ConfigureHotWalletAsync();
        var (tx, _) = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.SettlementVerifiedAt = null);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));

        // Causality: the clearance stamp is the single step that releases it.
        tx.SettlementVerifiedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync();
        await _sut.ExecuteAsync();

        Assert.True(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id && b.Type == BlockchainTransactionType.SWEEP));
    }

    // -------------------- T160c — withheld refund fees ride along with the sweep --------------------
    // Fixture fees (1.37 / 0.83 / 2.41 …) differ from every fallback setting
    // (TRON 2.0, Ethereum 0.05) so a fallback leaking in would show.

    [Fact]
    public async Task T160c_Confirmed_Deposit_Refund_Fees_In_The_Sale_Token_Are_Added_To_The_Sweep()
    {
        await ConfigureHotWalletAsync();
        var (tx, deposit) = await SeedDeliveredAsync(price: 100m, commission: 2m);
        // Over-payment refund (excess 7.00 → net 5.63, fee 1.37) and a second
        // transfer refunded whole minus its fee (4.00 → 3.17, fee 0.83).
        AddRefund(tx, BlockchainTransactionType.EXCESS_REFUND, net: 5.63m, fee: 1.37m, from: deposit.Address);
        AddRefund(tx, BlockchainTransactionType.EXCESS_REFUND, net: 3.17m, fee: 0.83m, from: deposit.Address);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var sweep = await SingleSweepAsync(tx);
        Assert.Equal(104.20m, sweep.Amount);           // 102 escrow + 1.37 + 0.83
        Assert.Equal(2.20m, sweep.RefundFeeRecovered);
        Assert.Null(sweep.GasFee);
    }

    [Theory]
    [InlineData(BlockchainTransactionStatus.PENDING)]
    [InlineData(BlockchainTransactionStatus.DETECTED)]
    [InlineData(BlockchainTransactionStatus.FAILED)]
    public async Task T160c_A_Refund_That_Is_Not_Final_Keeps_Its_Whole_Base_On_The_Deposit(BlockchainTransactionStatus status)
    {
        await ConfigureHotWalletAsync();
        var (tx, deposit) = await SeedDeliveredAsync(price: 100m, commission: 2m);
        AddRefund(tx, BlockchainTransactionType.EXCESS_REFUND, net: 5.63m, fee: 1.37m, from: deposit.Address, status: status);
        AddRefund(tx, BlockchainTransactionType.INCORRECT_AMOUNT_REFUND, net: 9.59m, fee: 2.41m, from: deposit.Address);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var sweep = await SingleSweepAsync(tx);
        Assert.Equal(104.41m, sweep.Amount);           // only the confirmed under-payment refund's fee
        Assert.Equal(2.41m, sweep.RefundFeeRecovered);
    }

    [Fact]
    public async Task T160c_A_Wrong_Token_Refund_Fee_And_A_Hot_Wallet_Refund_Fee_Are_Not_Folded_In()
    {
        await ConfigureHotWalletAsync();
        var (tx, deposit) = await SeedDeliveredAsync(price: 100m, commission: 2m);
        // Withheld USDC — another token than this sweep moves.
        var wrong = AddRefund(tx, BlockchainTransactionType.WRONG_TOKEN_REFUND, net: 8.63m, fee: 1.37m, from: deposit.Address);
        wrong.ActualTokenAddress = "TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8";
        // Sent by the hot wallet: its fee never stayed on the deposit.
        AddRefund(tx, BlockchainTransactionType.EXCESS_REFUND, net: 3.17m, fee: 0.83m, from: HotWalletAddress);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var sweep = await SingleSweepAsync(tx);
        Assert.Equal(102m, sweep.Amount);
        Assert.Null(sweep.RefundFeeRecovered);
    }

    [Fact]
    public async Task T160c_Fees_A_Collection_Already_Carries_Are_Not_Moved_Twice()
    {
        await ConfigureHotWalletAsync();
        var (tx, deposit) = await SeedDeliveredAsync(price: 100m, commission: 2m);
        AddRefund(tx, BlockchainTransactionType.EXCESS_REFUND, net: 5.63m, fee: 1.37m, from: deposit.Address);
        AddRefund(tx, BlockchainTransactionType.EXCESS_REFUND, net: 3.17m, fee: 0.83m, from: deposit.Address);
        AddCollection(tx, deposit, 0.83m, BlockchainTransactionStatus.CONFIRMED);
        AddCollection(tx, deposit, 1.37m, BlockchainTransactionStatus.FAILED); // moved nothing
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var sweep = await SingleSweepAsync(tx);
        Assert.Equal(103.37m, sweep.Amount);
        Assert.Equal(1.37m, sweep.RefundFeeRecovered);
    }

    private BlockchainTransaction AddRefund(
        Transaction tx,
        BlockchainTransactionType type,
        decimal net,
        decimal fee,
        string from,
        BlockchainTransactionStatus status = BlockchainTransactionStatus.CONFIRMED)
    {
        var row = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Type = type,
            TxHash = status == BlockchainTransactionStatus.PENDING ? null : "refund-" + Guid.NewGuid().ToString("N"),
            FromAddress = from,
            ToAddress = tx.BuyerRefundAddress!,
            Amount = net,
            GasFee = fee,
            Token = StablecoinType.USDT,
            ActualTokenAddress = type == BlockchainTransactionType.WRONG_TOKEN_REFUND
                ? "TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8"
                : null,
            Status = status,
            ConfirmationCount = status == BlockchainTransactionStatus.CONFIRMED ? 20 : 0,
            ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED ? _clock.GetUtcNow().UtcDateTime : null,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<BlockchainTransaction>().Add(row);
        return row;
    }

    private void AddCollection(Transaction tx, PaymentAddress deposit, decimal amount, BlockchainTransactionStatus status) =>
        _db.Set<BlockchainTransaction>().Add(new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = deposit.Id,
            Type = BlockchainTransactionType.REFUND_FEE_SWEEP,
            TxHash = "collect-" + Guid.NewGuid().ToString("N"),
            FromAddress = deposit.Address,
            ToAddress = HotWalletAddress,
            Amount = amount,
            RefundFeeRecovered = amount,
            Token = StablecoinType.USDT,
            Status = status,
            ConfirmationCount = status == BlockchainTransactionStatus.CONFIRMED ? 20 : 0,
            ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED ? _clock.GetUtcNow().UtcDateTime : null,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        });

    private Task<BlockchainTransaction> SingleSweepAsync(Transaction tx) =>
        _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == tx.Id && b.Type == BlockchainTransactionType.SWEEP);

    [Fact]
    public async Task ReversedDelivery_IsSkipped()
    {
        await ConfigureHotWalletAsync();
        var (tx, _) = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.DeliveryReversedAt = _clock.GetUtcNow().UtcDateTime);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    [Fact]
    public async Task MissingDepositAddress_IsSkipped()
    {
        await ConfigureHotWalletAsync();
        var (tx, _) = await SeedDeliveredAsync(price: 100m, commission: 2m, withDeposit: false);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    [Fact]
    public async Task ExistingSweepRow_IsNotDuplicated()
    {
        await ConfigureHotWalletAsync();
        var (tx, deposit) = await SeedDeliveredAsync(price: 100m, commission: 2m);
        _db.Set<BlockchainTransaction>().Add(NewSweepRow(tx, deposit));
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var count = await _db.Set<BlockchainTransaction>().CountAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SWEEP);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RunTwice_QueuesExactlyOneSweepRow()
    {
        await ConfigureHotWalletAsync();
        var (tx, _) = await SeedDeliveredAsync(price: 100m, commission: 2m);

        await _sut.ExecuteAsync();
        await _sut.ExecuteAsync();

        var count = await _db.Set<BlockchainTransaction>().CountAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SWEEP);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SecondSweepRow_ForSameTransaction_IsRejectedByUniqueIndex()
    {
        // WP3 money-safety backstop. The filtered unique index
        // (TransactionId WHERE Type='SWEEP') guarantees a transaction can never
        // hold two SWEEP rows, so a producer insert that slips past the
        // [DisableConcurrentExecution] lock cannot double-sweep the deposit.
        var (tx, deposit) = await SeedDeliveredAsync(price: 100m, commission: 2m);

        _db.Set<BlockchainTransaction>().Add(NewSweepRow(tx, deposit));
        await _db.SaveChangesAsync();

        _db.Set<BlockchainTransaction>().Add(NewSweepRow(tx, deposit));
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task SweepRow_WithNullPaymentAddressId_IsRejectedByCheckConstraint()
    {
        // CK_BlockchainTransactions_Type_Sweep enforces the deposit-anchored
        // invariant (PaymentAddressId NOT NULL). A SWEEP row built from the
        // outbound refund/payout template (PaymentAddressId = null) must be
        // rejected at the database, in SQLite as well as SQL Server.
        var (tx, deposit) = await SeedDeliveredAsync(price: 100m, commission: 2m);
        var bad = NewSweepRow(tx, deposit);
        bad.PaymentAddressId = null;

        _db.Set<BlockchainTransaction>().Add(bad);
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentInsertRace_SwallowsDuplicate_AndDoesNotDoubleSweep()
    {
        // Drives the producer's catch(DbUpdateException) backstop (WP1 F1
        // pattern). A competing tick commits the SWEEP row in the window between
        // this tick's AnyAsync guard and its SaveChanges, so the filtered unique
        // index rejects this insert. The catch must detach, re-query, confirm
        // the row now exists, and swallow as an idempotent no-op.
        await ConfigureHotWalletAsync();
        var (tx, deposit) = await SeedDeliveredAsync(price: 100m, commission: 2m);
        var logger = new ListLogger<SweepQueueJob>();

        await using var raceDb = new RaceDbContext(_options, injectBeforeSave: async () =>
        {
            await using var competing = new AppDbContext(_options);
            competing.Set<BlockchainTransaction>().Add(NewSweepRow(tx, deposit));
            await competing.SaveChangesAsync();
        });
        var sut = new SweepQueueJob(raceDb, _walletAddresses, _clock, logger);

        await sut.ExecuteAsync();   // must not throw

        var count = await _db.Set<BlockchainTransaction>().CountAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SWEEP);
        Assert.Equal(1, count);
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("concurrent insert race"));
    }

    [Fact]
    public async Task NonDuplicateDbUpdateException_IsRethrown_NotMasked()
    {
        // The catch must only swallow when a SWEEP row genuinely now exists. An
        // unrelated DbUpdateException (no row created) must surface unchanged.
        await ConfigureHotWalletAsync();
        var (tx, _) = await SeedDeliveredAsync(price: 100m, commission: 2m);
        await using var throwingDb = new RaceDbContext(_options, throwUnrelated: true);
        var sut = new SweepQueueJob(throwingDb, _walletAddresses, _clock, NullLogger<SweepQueueJob>.Instance);

        await Assert.ThrowsAsync<DbUpdateException>(() => sut.ExecuteAsync());

        Assert.Equal(0, await _db.Set<BlockchainTransaction>().CountAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SWEEP));
    }

    private BlockchainTransaction NewSweepRow(Transaction tx, PaymentAddress deposit) => new()
    {
        Id = Guid.NewGuid(),
        TransactionId = tx.Id,
        PaymentAddressId = deposit.Id,
        Type = BlockchainTransactionType.SWEEP,
        FromAddress = deposit.Address,
        ToAddress = HotWalletAddress,
        Amount = tx.TotalAmount,
        Token = StablecoinType.USDT,
        ActualTokenAddress = null,
        GasFee = null,
        Status = BlockchainTransactionStatus.PENDING,
        ConfirmationCount = 0,
        RetryCount = 0,
        CreatedAt = _clock.GetUtcNow().UtcDateTime,
    };

    /// <summary>
    /// The sweep destination is deployment configuration now, not an
    /// admin-editable SystemSetting (05 §3.3, owner decision 2026-09-16), so
    /// the fixture sets the provider rather than a row. Kept async so the
    /// call sites read unchanged.
    /// </summary>
    private Task ConfigureHotWalletAsync()
    {
        _walletAddresses.HotWalletAddress = HotWalletAddress;
        return Task.CompletedTask;
    }

    /// <summary>Mutable stand-in for the configuration-backed provider.</summary>
    private sealed class StubWalletAddresses : IPlatformWalletAddressProvider
    {
        public string? HotWalletAddress { get; set; }
        public string? ColdWalletAddress { get; set; }

        /// <summary>T148 — Ethereum's own hot wallet; null = unconfigured.</summary>
        public string? EthereumHotWalletAddress { get; set; }

        public PlatformWalletAddresses For(BlockchainNetwork network) => network switch
        {
            BlockchainNetwork.TRON => new(network, HotWalletAddress, ColdWalletAddress),
            _ => new(network, EthereumHotWalletAddress, null),
        };
    }

    private async Task<(Transaction Tx, PaymentAddress Deposit)> SeedDeliveredAsync(
        decimal price, decimal commission, Action<Transaction>? configure = null, bool withDeposit = true,
        BlockchainNetwork depositNetwork = BlockchainNetwork.TRON)
    {
        // Unique per call (SteamId, (Network, Address) and (Network,
        // HdWalletIndex) are unique) so a test can seed a whole batch.
        var seq = ++_seedSequence;
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = $"7656119800{seq * 2:D7}",
            SteamDisplayName = "Seller",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = $"7656119800{(seq * 2) + 1:D7}",
            SteamDisplayName = "Buyer",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<User>().AddRange(seller, buyer);

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.ITEM_DELIVERED,
            SellerId = seller.Id,
            BuyerId = buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000923",
            BuyerRefundAddress = "TBuyerRefund000000000000000000000000",
            ItemAssetId = "asset-1",
            ItemClassId = "cls",
            ItemName = "AK-47 | Redline",
            DeliveredBuyerAssetId = "delivered-asset-1",
            StablecoinType = StablecoinType.USDT,
            Price = price,
            CommissionRate = 0.02m,
            CommissionAmount = commission,
            TotalAmount = price + commission,
            SellerPayoutAddress = "TSellerPayout00000000000000000000000",
            ItemDeliveredAt = _clock.GetUtcNow().UtcDateTime,
            // T129 — the sweep waits for the same settlement clearance the
            // payout does: until the end-of-window re-read passes, the deposit
            // is still the source a reversal refund would draw from.
            PayoutEligibleAt = _clock.GetUtcNow().UtcDateTime.AddDays(-8),
            SettlementVerifiedAt = _clock.GetUtcNow().UtcDateTime,
        };
        configure?.Invoke(tx);
        _db.Set<Transaction>().Add(tx);

        PaymentAddress? deposit = null;
        if (withDeposit)
        {
            deposit = new PaymentAddress
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                Network = depositNetwork,
                Address = seq == 1 ? "TDepositSweepFixture000000000000000" : $"TDepositSweepFixture{seq:D15}",
                HdWalletIndex = 6 + seq,
                ExpectedAmount = price + commission,
                ExpectedToken = StablecoinType.USDT,
                MonitoringStatus = MonitoringStatus.ACTIVE,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                UpdatedAt = _clock.GetUtcNow().UtcDateTime,
                RowVersion = new byte[8],
            };
            _db.Set<PaymentAddress>().Add(deposit);
        }

        await _db.SaveChangesAsync();
        return (tx, deposit!);
    }

    /// <summary>
    /// Test seam exercising <see cref="SweepQueueJob"/>'s
    /// catch(DbUpdateException) backstop (WP1 F1 pattern). On the first
    /// SaveChanges that adds a SWEEP row it either commits a competing row
    /// out-of-band (so the real filtered unique index rejects the job's insert →
    /// swallow branch) or throws an unrelated DbUpdateException with no row
    /// created (→ re-throw branch).
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
            var addingSweep = !_fired && ChangeTracker.Entries<BlockchainTransaction>()
                .Any(e => e.State == EntityState.Added
                    && e.Entity.Type == BlockchainTransactionType.SWEEP);
            if (addingSweep)
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
