using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Transfers;

/// <summary>
/// Unit coverage for <see cref="SellerPayoutQueueJob"/> (WP1 — 02 §4.7,
/// 03 §2.4). Confirms the gas-fee-protection net is queued as a PENDING
/// SELLER_PAYOUT row, the gas estimate is snapshotted, and held / disputed /
/// non-delivered / already-paid / addressless transactions are skipped.
/// Extended by the T126 validation (finding F1) with the 02 §4.5.1 settlement
/// gate: delivery alone never releases the payout.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SellerPayoutQueueJobTests : IDisposable
{
    static SellerPayoutQueueJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly AppDbContext _db;
    private readonly StubGasFeeSettingsProvider _settings;
    private readonly FakeTimeProvider _clock;
    private readonly SellerPayoutQueueJob _sut;

    public SellerPayoutQueueJobTests()
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
                PayoutGasFeeEstimateUsdt: 0.50m),
        };
        _clock = new FakeTimeProvider();
        _clock.SetUtcNow(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        _sut = new SellerPayoutQueueJob(
            _db,
            new RefundDecisionService(_settings),
            _settings,
            _clock,
            NullLogger<SellerPayoutQueueJob>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GasAboveThreshold_QueuesPendingPayout_WithNetAmountAndGasSnapshot()
    {
        // price 100, commission 2 → threshold 0.20; gasFee 0.50 > 0.20 →
        // overage 0.30 → net 99.70 (04 §7.3 worked example).
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m);

        await _sut.ExecuteAsync();

        var payout = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT);
        Assert.Equal(BlockchainTransactionStatus.PENDING, payout.Status);
        Assert.Equal(99.70m, payout.Amount);
        Assert.Equal(0.50m, payout.GasFee);
        Assert.Equal(tx.SellerPayoutAddress, payout.ToAddress);
        Assert.Equal(StablecoinType.USDT, payout.Token);
        Assert.Null(payout.PaymentAddressId);
        Assert.Null(payout.ActualTokenAddress);
        Assert.Equal(string.Empty, payout.FromAddress);
        Assert.Null(payout.NextAttemptAt);
    }

    [Fact]
    public async Task GasBelowThreshold_PaysFullPrice()
    {
        // commission 10 → threshold 1.0; gasFee 0.50 ≤ 1.0 → platform absorbs,
        // net = price.
        var tx = await SeedDeliveredAsync(price: 100m, commission: 10m);

        await _sut.ExecuteAsync();

        var payout = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT);
        Assert.Equal(100m, payout.Amount);
        Assert.Equal(0.50m, payout.GasFee);
    }

    [Fact]
    public async Task HeldTransaction_IsSkipped()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
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
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.HasActiveDispute = true);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    [Fact]
    public async Task NonDeliveredTransaction_IsSkipped()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.Status = TransactionStatus.PAYMENT_RECEIVED);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    /// <summary>
    /// T126 validation finding F1 — the settlement gate (02 §4.5.1). A NULL
    /// <c>PayoutEligibleAt</c> means the settlement window was never armed, and
    /// that is the state every ITEM_DELIVERED transaction is in until T129
    /// computes the column. Paying here would hand the seller their money while
    /// Steam still lets them reverse the trade for 7 days — item back to the
    /// seller, money with the seller, buyer with neither.
    /// </summary>
    [Fact]
    public async Task NullPayoutEligibleAt_IsSkipped()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.PayoutEligibleAt = null);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    /// <summary>
    /// The window is armed but still open: waiting is the whole point, so the
    /// tick must pass over it rather than round the remaining time down.
    /// </summary>
    [Fact]
    public async Task PayoutEligibleAt_InTheFuture_IsSkipped()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.PayoutEligibleAt = _clock.GetUtcNow().UtcDateTime.AddSeconds(1));

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));

        // And the causality, so this test fails for the right reason: the clock
        // reaching the eligibility instant is the single step that releases it.
        _clock.Advance(TimeSpan.FromSeconds(1));
        await _sut.ExecuteAsync();

        Assert.True(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));
    }

    /// <summary>
    /// T129 — the second half of the settlement gate. An elapsed window says the
    /// reversal period has closed, not that nobody used it; only
    /// <c>SettlementVerifiedAt</c> says that. Without this check a reversed
    /// transaction would still have its payout broadcast, and the money would be
    /// gone before the COMPLETED guard ever got to refuse the transition.
    /// </summary>
    [Fact]
    public async Task ElapsedWindow_WithoutSettlementVerification_IsSkipped()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.SettlementVerifiedAt = null);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));

        // Causality: the stamp is the single step that releases it.
        tx.SettlementVerifiedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync();
        await _sut.ExecuteAsync();

        Assert.True(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));
    }

    /// <summary>
    /// T129 — a reversal detected during the window. The transaction is on its
    /// way to REFUNDED; paying the seller now would pay the person who took the
    /// item back.
    /// </summary>
    [Fact]
    public async Task ReversedDelivery_IsSkipped_EvenWithSettlementStamp()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.DeliveryReversedAt = _clock.GetUtcNow().UtcDateTime);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    [Fact]
    public async Task ExistingPayoutRow_IsNotDuplicated()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m);
        _db.Set<BlockchainTransaction>().Add(new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Type = BlockchainTransactionType.SELLER_PAYOUT,
            FromAddress = string.Empty,
            ToAddress = tx.SellerPayoutAddress,
            Amount = 99.70m,
            Token = StablecoinType.USDT,
            GasFee = 0.50m,
            Status = BlockchainTransactionStatus.PENDING,
            ConfirmationCount = 0,
            RetryCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var count = await _db.Set<BlockchainTransaction>().CountAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SecondSellerPayoutRow_ForSameTransaction_IsRejectedByUniqueIndex()
    {
        // WP1 F1 money-safety backstop. The filtered unique index
        // (TransactionId WHERE Type='SELLER_PAYOUT') guarantees a transaction
        // can never hold two SELLER_PAYOUT rows, so a producer insert that
        // slips past the [DisableConcurrentExecution] lock cannot double-pay.
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m);

        _db.Set<BlockchainTransaction>().Add(NewSellerPayoutRow(tx));
        await _db.SaveChangesAsync();

        _db.Set<BlockchainTransaction>().Add(NewSellerPayoutRow(tx));
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task RunTwice_QueuesExactlyOneSellerPayoutRow()
    {
        // End-to-end producer idempotency: a second tick on the same delivered
        // transaction is a no-op (AnyAsync guard), and the unique index ensures
        // the invariant even if that guard were ever bypassed.
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m);

        await _sut.ExecuteAsync();
        await _sut.ExecuteAsync();

        var count = await _db.Set<BlockchainTransaction>().CountAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ConcurrentInsertRace_SwallowsDuplicate_AndDoesNotDoublePay()
    {
        // Drives the producer's catch(DbUpdateException) backstop (WP1 F1). A
        // competing tick commits the SELLER_PAYOUT row in the window between
        // this tick's AnyAsync guard and its SaveChanges, so the filtered
        // unique index rejects this insert. The catch must detach, re-query,
        // confirm the row now exists, and swallow as an idempotent no-op —
        // exactly one row, no escaping exception.
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m);
        var logger = new ListLogger<SellerPayoutQueueJob>();

        // Injected mid-SaveChanges: a separate context on the same connection
        // commits the competing payout, mirroring a parallel tick that won the
        // race after this tick's idempotency check already passed.
        await using var raceDb = new RaceDbContext(_options, injectBeforeSave: async () =>
        {
            await using var competing = new AppDbContext(_options);
            competing.Set<BlockchainTransaction>().Add(NewSellerPayoutRow(tx));
            await competing.SaveChangesAsync();
        });
        var sut = new SellerPayoutQueueJob(
            raceDb, new RefundDecisionService(_settings), _settings, _clock, logger);

        await sut.ExecuteAsync();   // must not throw

        var count = await _db.Set<BlockchainTransaction>().CountAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT);
        Assert.Equal(1, count);
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("concurrent insert race"));
    }

    [Fact]
    public async Task NonDuplicateDbUpdateException_IsRethrown_NotMasked()
    {
        // The catch must only swallow when a SELLER_PAYOUT row genuinely now
        // exists. An unrelated DbUpdateException (no row created) must surface
        // unchanged — never be masked as an idempotent no-op. Locks in the
        // `if (!nowQueued) throw` branch.
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m);
        await using var throwingDb = new RaceDbContext(_options, throwUnrelated: true);
        var sut = new SellerPayoutQueueJob(
            throwingDb, new RefundDecisionService(_settings), _settings, _clock,
            NullLogger<SellerPayoutQueueJob>.Instance);

        await Assert.ThrowsAsync<DbUpdateException>(() => sut.ExecuteAsync());

        Assert.Equal(0, await _db.Set<BlockchainTransaction>().CountAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));
    }

    [Fact]
    public async Task EmptySellerPayoutAddress_IsSkipped()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.SellerPayoutAddress = string.Empty);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    private BlockchainTransaction NewSellerPayoutRow(Transaction tx) => new()
    {
        Id = Guid.NewGuid(),
        TransactionId = tx.Id,
        Type = BlockchainTransactionType.SELLER_PAYOUT,
        FromAddress = string.Empty,
        ToAddress = tx.SellerPayoutAddress,
        Amount = 99.70m,
        Token = StablecoinType.USDT,
        GasFee = 0.50m,
        Status = BlockchainTransactionStatus.PENDING,
        ConfirmationCount = 0,
        RetryCount = 0,
        CreatedAt = _clock.GetUtcNow().UtcDateTime,
    };

    private async Task<Transaction> SeedDeliveredAsync(
        decimal price, decimal commission, Action<Transaction>? configure = null)
    {
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000811",
            SteamDisplayName = "Seller",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000812",
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
            TargetBuyerSteamId = "76561198000000913",
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
            // 02 §4.5.1 — the settlement window has elapsed. Set by default so
            // every pre-existing case still exercises what it was written for;
            // the two gate tests below override it. T129 computes this column on
            // entry to ITEM_DELIVERED; until then nothing writes it in
            // production, which is exactly why the gate must fail closed.
            PayoutEligibleAt = _clock.GetUtcNow().UtcDateTime.AddDays(-8),
            // T129 — and the window having elapsed is only half of it: the
            // end-of-window re-read is what says the trade was not reversed.
            // Set by default for the same reason as the column above; the two
            // T129 gate tests below override it.
            SettlementVerifiedAt = _clock.GetUtcNow().UtcDateTime,
        };
        configure?.Invoke(tx);

        _db.Set<Transaction>().Add(tx);
        await _db.SaveChangesAsync();
        return tx;
    }

    private sealed class StubGasFeeSettingsProvider : IGasFeeSettingsProvider
    {
        public GasFeeSettings Settings { get; set; } =
            new(0.10m, 2m, 2m, 0.50m);

        public Task<GasFeeSettings> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Settings);
    }

    /// <summary>
    /// Test seam exercising <see cref="SellerPayoutQueueJob"/>'s
    /// catch(DbUpdateException) backstop (WP1 F1). On the first SaveChanges that
    /// adds a SELLER_PAYOUT row it either commits a competing row out-of-band
    /// (so the real filtered unique index rejects the job's insert → swallow
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
            var addingPayout = !_fired && ChangeTracker.Entries<BlockchainTransaction>()
                .Any(e => e.State == EntityState.Added
                    && e.Entity.Type == BlockchainTransactionType.SELLER_PAYOUT);
            if (addingPayout)
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
