using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
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
    private readonly SweepQueueJob _sut;

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

        _sut = new SweepQueueJob(_db, _clock, NullLogger<SweepQueueJob>.Instance);
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
        Assert.Null(sweep.NextAttemptAt);
    }

    [Fact]
    public async Task HotWalletUnconfigured_QueuesNoSweep()
    {
        // The seeded reconciliation.hot_wallet_address is the "NONE" sentinel —
        // leave it; the job must skip the whole run rather than queue a row
        // with a bogus destination.
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
        var sut = new SweepQueueJob(raceDb, _clock, logger);

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
        var sut = new SweepQueueJob(throwingDb, _clock, NullLogger<SweepQueueJob>.Instance);

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

    private async Task ConfigureHotWalletAsync()
    {
        // SystemSettingSeed ships reconciliation.hot_wallet_address as the
        // "NONE" sentinel via HasData, so EnsureCreated already left a row;
        // flip it to a real address rather than insert a duplicate
        // (UQ_SystemSettings_Key).
        const string key = "reconciliation.hot_wallet_address";
        var existing = await _db.Set<SystemSetting>().FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null)
        {
            _db.Set<SystemSetting>().Add(new SystemSetting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = HotWalletAddress,
                IsConfigured = true,
                DataType = "string",
                Category = "Monitoring",
                Description = "Sweep fixture",
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                UpdatedAt = _clock.GetUtcNow().UtcDateTime,
                RowVersion = new byte[8],
            });
        }
        else
        {
            existing.Value = HotWalletAddress;
            existing.IsConfigured = true;
            existing.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        }
        await _db.SaveChangesAsync();
    }

    private async Task<(Transaction Tx, PaymentAddress Deposit)> SeedDeliveredAsync(
        decimal price, decimal commission, Action<Transaction>? configure = null, bool withDeposit = true)
    {
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000821",
            SteamDisplayName = "Seller",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000822",
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
                Address = "TDepositSweepFixture000000000000000",
                HdWalletIndex = 7,
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
