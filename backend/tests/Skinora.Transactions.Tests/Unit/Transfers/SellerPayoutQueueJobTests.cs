using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;
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
/// gate: delivery alone never releases the payout. Extended again for
/// PayoutStallsOnNonPositiveNet: a non-positive net is deferred on a backoff,
/// stays out of the window meanwhile, and alerts the admins once.
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
    private readonly StubChargedGasFeeResolver _gasFee;
    private readonly RecordingOutbox _outbox = new();
    private readonly FakeTimeProvider _clock;
    private readonly SellerPayoutQueueJob _sut;
    private int _seedCount;

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
                PayoutGasFeeEstimateUsdt: 0.50m, MaxChargedGasFeeUsdt: 10m),
        };
        _gasFee = new StubChargedGasFeeResolver { PayoutFee = 0.50m };
        _clock = new FakeTimeProvider();
        _clock.SetUtcNow(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        _sut = new SellerPayoutQueueJob(
            _db,
            new RefundDecisionService(_settings),
            _gasFee,
            _outbox,
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
    public async Task RuntimeEstimate_FlowsIntoSplitAndSnapshot()
    {
        // Prova-GasFeeChargedIsFixedGuess: the split and the GasFee snapshot
        // must carry the RESOLVED runtime value, not the static 0.50 setting.
        // 0.02 ≤ threshold 0.20 → platform absorbs, net = price; snapshot 0.02.
        _gasFee.PayoutFee = 0.02m;
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m);

        await _sut.ExecuteAsync();

        var payout = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT);
        Assert.Equal(100m, payout.Amount);
        Assert.Equal(0.02m, payout.GasFee);
        var call = Assert.Single(_gasFee.PayoutCalls);
        Assert.Equal(tx.SellerPayoutAddress, call.To);
        Assert.Equal(100m, call.Amount);
        Assert.Equal(StablecoinType.USDT, call.Token);
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
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));
    }

    [Fact]
    public async Task DisputedTransaction_IsSkipped()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.HasActiveDispute = true);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));
    }

    [Fact]
    public async Task NonDeliveredTransaction_IsSkipped()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.Status = TransactionStatus.PAYMENT_RECEIVED);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));
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
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));
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
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));

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
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));

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
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));
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
            raceDb, new RefundDecisionService(_settings), _gasFee, _outbox, _clock, logger);

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
            throwingDb, new RefundDecisionService(_settings), _gasFee, _outbox, _clock,
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
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));
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

    /// <summary>
    /// Owner decision 2026-09-17 — the hot wallet pays out money it has
    /// already received for that transaction, so an unswept (or still
    /// in-flight) deposit holds the payout instead of drawing on an operating
    /// balance the platform would have to park there (DEPLOY_RUNBOOK §I).
    /// </summary>
    [Fact]
    public async Task SweepNotQueued_IsSkipped()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, sweepStatus: null);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));
    }

    [Fact]
    public async Task SweepStillPending_IsSkipped_ThenQueuedOnceItConfirms()
    {
        var tx = await SeedDeliveredAsync(
            price: 100m, commission: 2m, sweepStatus: BlockchainTransactionStatus.PENDING);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));

        // Causality: confirming that one sweep is the single step that releases
        // the payout — nothing else about the transaction changes.
        var sweep = await _db.Set<BlockchainTransaction>()
            .SingleAsync(b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SWEEP);
        sweep.Status = BlockchainTransactionStatus.CONFIRMED;
        sweep.TxHash = $"sweep-{Guid.NewGuid():N}";
        sweep.ConfirmationCount = 20;
        sweep.ConfirmedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.True(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));
    }

    /// <summary>
    /// The gate is "this transaction's own sweep", not "a sweep landed
    /// somewhere". Two layers enforce it — the candidate query walks the
    /// navigation and the re-read carries an explicit
    /// <c>TransactionId</c> term — and with a single transaction in the fixture
    /// neither scope is observable: dropping the re-read's term, or the
    /// candidate clause, leaves every other case green. This one seeds two
    /// delivered transactions, funds only the first, and pins that the hot
    /// wallet pays for the money it actually received.
    /// </summary>
    [Fact]
    public async Task AnotherTransactionsConfirmedSweep_DoesNotFundThisPayout()
    {
        var funded = await SeedDeliveredAsync(price: 100m, commission: 2m);
        var unswept = await SeedDeliveredAsync(
            price: 100m, commission: 2m, sweepStatus: null);

        await _sut.ExecuteAsync();

        Assert.True(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == funded.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));
        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == unswept.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT));
    }

    // ---------- Non-positive net → deferral (owner decision 2026-10-01) ----------
    //
    // PayoutStallsOnNonPositiveNet. price 1, commission 0.02 → protection share
    // 0.002; a 3.00 estimate (mainnet burn ≈ 2.2–4.4 USDT) leaves
    // 1 − (3 − 0.002) = −1.998. The backoff and the alert threshold are written
    // out as literals: their size IS the requirement, and a test that read them
    // back from the job would move with them.

    private static readonly DateTime T0 = new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task NonPositivePayout_QueuesNoRow_AndIsDeferredAnHour()
    {
        _gasFee.PayoutFee = 3.00m;
        var tx = await SeedDeliveredAsync(price: 1m, commission: 0.02m);

        await _sut.ExecuteAsync();

        Assert.False(await HasPayoutRowAsync(tx.Id));
        var row = await ReloadAsync(tx.Id);
        Assert.Equal(1, row.PayoutDeferralCount);
        Assert.Equal(T0.AddHours(1), row.PayoutDeferredUntil);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task ExactlyZeroPayout_IsDeferredToo()
    {
        // 1 − (1.002 − 0.002) = 0: nothing to send is as unsendable as less.
        _gasFee.PayoutFee = 1.002m;
        var tx = await SeedDeliveredAsync(price: 1m, commission: 0.02m);

        await _sut.ExecuteAsync();

        Assert.False(await HasPayoutRowAsync(tx.Id));
        Assert.Equal(1, (await ReloadAsync(tx.Id)).PayoutDeferralCount);
    }

    [Fact]
    public async Task DeferredPayout_IsNotRepricedBeforeItsRetryTime_AndIsAtIt()
    {
        _gasFee.PayoutFee = 3.00m;
        var tx = await SeedDeliveredAsync(price: 1m, commission: 0.02m);
        await _sut.ExecuteAsync();                      // deferral 1 → 13:00

        _clock.Advance(TimeSpan.FromMinutes(59));
        await _sut.ExecuteAsync();
        Assert.Single(_gasFee.PayoutCalls);            // 12:59 — left alone

        _clock.Advance(TimeSpan.FromMinutes(1));
        await _sut.ExecuteAsync();                      // 13:00 — due
        Assert.Equal(2, _gasFee.PayoutCalls.Count);
        var row = await ReloadAsync(tx.Id);
        Assert.Equal(2, row.PayoutDeferralCount);
        Assert.Equal(T0.AddHours(1 + 4), row.PayoutDeferredUntil);
    }

    [Fact]
    public async Task DeferralBackoff_Is1h_4h_12h_ThenDaily()
    {
        _gasFee.PayoutFee = 3.00m;
        var tx = await SeedDeliveredAsync(price: 1m, commission: 0.02m);

        var waits = new List<TimeSpan>();
        for (var i = 0; i < 6; i++)
        {
            var at = _clock.GetUtcNow().UtcDateTime;
            await _sut.ExecuteAsync();
            var until = (await ReloadAsync(tx.Id)).PayoutDeferredUntil!.Value;
            waits.Add(until - at);
            _clock.Advance(until - at);
        }

        Assert.Equal(
            [
                TimeSpan.FromHours(1), TimeSpan.FromHours(4), TimeSpan.FromHours(12),
                TimeSpan.FromHours(24), TimeSpan.FromHours(24), TimeSpan.FromHours(24),
            ],
            waits);
        Assert.Equal(6, (await ReloadAsync(tx.Id)).PayoutDeferralCount);
    }

    [Fact]
    public async Task ThirdDeferral_AlertsTheAdminsOnce_WithTheSplitInputs()
    {
        _gasFee.PayoutFee = 3.00m;
        var tx = await SeedDeliveredAsync(price: 1m, commission: 0.02m);

        await RunAtRetryAsync(tx.Id, times: 2);
        Assert.Empty(_outbox.Published);

        await AdvanceToRetryAsync(tx.Id);
        var thirdAt = _clock.GetUtcNow().UtcDateTime;
        await _sut.ExecuteAsync();

        var alert = Assert.IsType<SellerPayoutDeferredEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(tx.Id, alert.TransactionId);
        Assert.Equal(tx.SellerId, alert.SellerId);
        Assert.Equal(1m, alert.Price);
        Assert.Equal(0.02m, alert.CommissionAmount);
        Assert.Equal(3.00m, alert.GasFeeEstimate);
        Assert.Equal(-1.998m, alert.ComputedPayout);
        Assert.Equal(3, alert.DeferralCount);
        Assert.Equal(thirdAt.AddHours(12), alert.NextAttemptAt);
        Assert.Equal(thirdAt, alert.OccurredAt);

        await RunAtRetryAsync(tx.Id, times: 3);         // 4th–6th: no second alert
        Assert.Single(_outbox.Published);
    }

    [Fact]
    public async Task ThirdDeferral_AlertCommitsInTheSameSaveAsTheStamp()
    {
        // Once the third deferral is written, its alert is in the database too —
        // not left in the change tracker for some later save to pick up.
        _gasFee.PayoutFee = 3.00m;
        var tx = await SeedDeliveredAsync(price: 1m, commission: 0.02m, configure: t =>
        {
            t.PayoutDeferralCount = 2;
            t.PayoutDeferredUntil = T0.AddMinutes(-1);
        });
        await using var jobDb = new AppDbContext(_options);
        var sut = new SellerPayoutQueueJob(
            jobDb, new RefundDecisionService(_settings), _gasFee,
            new DbContextOutbox(jobDb), _clock, NullLogger<SellerPayoutQueueJob>.Instance);

        await sut.ExecuteAsync();

        Assert.Equal(3, (await ReloadAsync(tx.Id)).PayoutDeferralCount);
        var message = await _db.Set<OutboxMessage>().AsNoTracking().SingleAsync();
        Assert.Equal(typeof(SellerPayoutDeferredEvent).FullName, message.EventType);
    }

    [Fact]
    public async Task TwentyStuckPayouts_DoNotStarveANewerOne()
    {
        // The #327 validation's measurement, kept: before the deferral these 20
        // held the oldest-first window every minute and the newer payout was
        // never queued (3 ticks → 0 rows, 60 estimates).
        _gasFee.PayoutFee = 3.00m;
        for (var i = 0; i < 20; i++)
        {
            var minute = i;
            await SeedDeliveredAsync(price: 1m, commission: 0.02m,
                configure: t => t.ItemDeliveredAt = T0.AddDays(-10).AddMinutes(minute));
        }
        var newer = await SeedDeliveredAsync(price: 100m, commission: 2m,
            configure: t => t.ItemDeliveredAt = T0.AddDays(-9));

        await _sut.ExecuteAsync();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _sut.ExecuteAsync();

        // 100 − (3 − 0.2) = 97.2; the 20 were priced once each, not again.
        Assert.Equal(97.20m, (await PayoutRowAsync(newer.Id)).Amount);
        Assert.Equal(21, _gasFee.PayoutCalls.Count);
    }

    [Fact]
    public async Task FreshPayout_GoesBeforeDeferredOnesThatAreDue()
    {
        // At their retry time these 20 are candidates again, and they were
        // delivered earlier: ordered by delivery alone they would fill the
        // batch and push the fresh payout out of every tick in which they
        // come due.
        _gasFee.PayoutFee = 3.00m;
        for (var i = 0; i < 20; i++)
        {
            var minute = i;
            await SeedDeliveredAsync(price: 1m, commission: 0.02m, configure: t =>
            {
                t.ItemDeliveredAt = T0.AddDays(-10).AddMinutes(minute);
                t.PayoutDeferralCount = 1;
                t.PayoutDeferredUntil = T0.AddMinutes(-1);
            });
        }
        var fresh = await SeedDeliveredAsync(price: 100m, commission: 2m,
            configure: t => t.ItemDeliveredAt = T0.AddDays(-9));

        await _sut.ExecuteAsync();

        Assert.Equal(97.20m, (await PayoutRowAsync(fresh.Id)).Amount);
    }

    [Fact]
    public async Task DuePayout_IsNotCrowdedOutByDeferredOnesStillWaiting()
    {
        // 20 rows deferred once and not due for another hour sort ahead of a row
        // deferred twice that is due now. Only the query's retry-time filter
        // keeps them out of the batch; without it they fill all 20 slots, the
        // loop skips each one, and the due payout waits for them.
        _gasFee.PayoutFee = 3.00m;
        for (var i = 0; i < 20; i++)
        {
            var minute = i;
            await SeedDeliveredAsync(price: 1m, commission: 0.02m, configure: t =>
            {
                t.ItemDeliveredAt = T0.AddDays(-10).AddMinutes(minute);
                t.PayoutDeferralCount = 1;
                t.PayoutDeferredUntil = T0.AddHours(1);
            });
        }
        var due = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
        {
            t.ItemDeliveredAt = T0.AddDays(-9);
            t.PayoutDeferralCount = 2;
            t.PayoutDeferredUntil = T0.AddMinutes(-1);
        });

        await _sut.ExecuteAsync();

        Assert.Equal(97.20m, (await PayoutRowAsync(due.Id)).Amount);
    }

    [Fact]
    public async Task SameDeferralCount_OldestDeliveryIsTakenFirst()
    {
        // The deferral count only goes in front of the delivery order; within
        // one count the window still takes the oldest delivery first. 21
        // payable rows for a 20-row batch: the one left for the next tick must
        // be the newest. It is seeded first so that insertion order cannot pass
        // for delivery order.
        var newest = await SeedDeliveredAsync(price: 100m, commission: 2m,
            configure: t => t.ItemDeliveredAt = T0.AddDays(-9));
        for (var i = 0; i < 20; i++)
        {
            var minute = i;
            await SeedDeliveredAsync(price: 100m, commission: 2m,
                configure: t => t.ItemDeliveredAt = T0.AddDays(-10).AddMinutes(minute));
        }

        await _sut.ExecuteAsync();

        Assert.False(await HasPayoutRowAsync(newest.Id));
        Assert.Equal(20, await _db.Set<BlockchainTransaction>().CountAsync(
            b => b.Type == BlockchainTransactionType.SELLER_PAYOUT));
    }

    [Fact]
    public async Task DeferredPayout_IsQueuedAtTheFullPrice_OnceTheEstimateDrops()
    {
        _gasFee.PayoutFee = 3.00m;
        var tx = await SeedDeliveredAsync(price: 1m, commission: 0.02m);
        await _sut.ExecuteAsync();

        // Energy regenerated: the estimate is back under the platform's share.
        _gasFee.PayoutFee = 0.001m;
        _clock.Advance(TimeSpan.FromHours(1));
        await _sut.ExecuteAsync();

        var payout = await PayoutRowAsync(tx.Id);
        Assert.Equal(1m, payout.Amount);
        Assert.Equal(0.001m, payout.GasFee);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task RowChangedMidDeferral_DropsTheStampAndItsAlert_AndTheBatchGoesOn()
    {
        // The older row's third deferral raises the alert, and another writer
        // moves that row between the job's load and its save. RowVersion refuses
        // the stamp; the alert queued in the same unit of work must go with it
        // rather than ride along with the next candidate's payout insert.
        _gasFee.PayoutFee = 3.00m;
        var stuck = await SeedDeliveredAsync(price: 1m, commission: 0.02m, configure: t =>
        {
            t.ItemDeliveredAt = T0.AddDays(-10);
            t.PayoutDeferralCount = 2;
            t.PayoutDeferredUntil = T0.AddMinutes(-1);
        });
        var payable = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
        {
            t.ItemDeliveredAt = T0.AddDays(-9);
            t.PayoutDeferralCount = 2;
            t.PayoutDeferredUntil = T0.AddMinutes(-1);
        });

        await using var raceDb = new TransactionWriteRaceDbContext(_options);
        var logger = new ListLogger<SellerPayoutQueueJob>();
        var sut = new SellerPayoutQueueJob(
            raceDb, new RefundDecisionService(_settings), _gasFee,
            new DbContextOutbox(raceDb), _clock, logger);

        await sut.ExecuteAsync();   // must not throw

        Assert.Equal(2, (await ReloadAsync(stuck.Id)).PayoutDeferralCount);
        Assert.Equal(0, await _db.Set<OutboxMessage>().CountAsync());
        Assert.Equal(97.20m, (await PayoutRowAsync(payable.Id)).Amount);
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("changed while"));
    }

    private Task<Transaction> ReloadAsync(Guid id) =>
        _db.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == id);

    private Task<bool> HasPayoutRowAsync(Guid id) =>
        _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == id && b.Type == BlockchainTransactionType.SELLER_PAYOUT);

    private Task<BlockchainTransaction> PayoutRowAsync(Guid id) =>
        _db.Set<BlockchainTransaction>().AsNoTracking().SingleAsync(
            b => b.TransactionId == id && b.Type == BlockchainTransactionType.SELLER_PAYOUT);

    private async Task AdvanceToRetryAsync(Guid id)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        if ((await ReloadAsync(id)).PayoutDeferredUntil is { } until && until > now)
            _clock.Advance(until - now);
    }

    private async Task RunAtRetryAsync(Guid id, int times)
    {
        for (var i = 0; i < times; i++)
        {
            await AdvanceToRetryAsync(id);
            await _sut.ExecuteAsync();
        }
    }

    private async Task<Transaction> SeedDeliveredAsync(
        decimal price,
        decimal commission,
        Action<Transaction>? configure = null,
        BlockchainTransactionStatus? sweepStatus = BlockchainTransactionStatus.CONFIRMED)
    {
        // SteamId and the deposit address/index are unique in the schema, so
        // every seeded transaction needs its own — a case that seeds two
        // transactions (the funding gate's scope) would otherwise fail on the
        // index rather than on what it means to assert.
        var n = ++_seedCount;
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = $"7656119800000{n:D4}1",
            SteamDisplayName = "Seller",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = $"7656119800000{n:D4}2",
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

        // Owner decision 2026-09-17 — the payout is funded by this
        // transaction's own sweep, so the default fixture has one CONFIRMED.
        // Cases that probe the funding gate pass PENDING or null instead.
        if (sweepStatus is { } status)
        {
            var deposit = new PaymentAddress
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                Address = $"TDepositPayoutFixture{n:D14}",
                HdWalletIndex = 4242 + n,
                ExpectedAmount = tx.TotalAmount,
                ExpectedToken = StablecoinType.USDT,
                MonitoringStatus = MonitoringStatus.STOPPED,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                UpdatedAt = _clock.GetUtcNow().UtcDateTime,
                RowVersion = new byte[8],
            };
            _db.Set<PaymentAddress>().Add(deposit);
            _db.Set<BlockchainTransaction>().Add(new BlockchainTransaction
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                PaymentAddressId = deposit.Id,
                Type = BlockchainTransactionType.SWEEP,
                TxHash = status == BlockchainTransactionStatus.CONFIRMED
                    ? $"sweep-{Guid.NewGuid():N}"
                    : null,
                FromAddress = deposit.Address,
                ToAddress = "THotWalletPayoutFixture000000000000",
                Amount = tx.TotalAmount,
                Token = StablecoinType.USDT,
                Status = status,
                ConfirmationCount = status == BlockchainTransactionStatus.CONFIRMED ? 20 : 0,
                // CK_BlockchainTransactions_Status_Confirmed: a CONFIRMED row
                // carries both the count and the stamp.
                ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED
                    ? _clock.GetUtcNow().UtcDateTime
                    : null,
                RetryCount = 0,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
            });
        }

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
        public List<(string To, decimal Amount, StablecoinType Token)> PayoutCalls { get; } = [];

        public Task<ResolvedGasFee> ResolveRefundFeeAsync(
            string? fromDepositAddress, string toAddress, decimal amount,
            StablecoinType token, CancellationToken cancellationToken) =>
            Task.FromResult(new ResolvedGasFee(RefundFee, Source));

        public Task<ResolvedGasFee> ResolvePayoutFeeAsync(
            string toAddress, decimal amount, StablecoinType token,
            CancellationToken cancellationToken)
        {
            PayoutCalls.Add((toAddress, amount, token));
            return Task.FromResult(new ResolvedGasFee(PayoutFee, Source));
        }
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

    private sealed class RecordingOutbox : IOutboxService
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Mirrors the production outbox's one property this suite needs: the
    /// message joins the caller's unit of work (05 §5.1) and commits with it,
    /// or not at all.
    /// </summary>
    private sealed class DbContextOutbox : IOutboxService
    {
        private readonly AppDbContext _db;

        public DbContextOutbox(AppDbContext db) => _db = db;

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            _db.OutboxMessages.Add(new OutboxMessage
            {
                Id = domainEvent.EventId,
                EventType = domainEvent.GetType().FullName!,
                Payload = "{}",
                Status = OutboxMessageStatus.PENDING,
                CreatedAt = domainEvent.OccurredAt,
                Sequence = 1,
            });
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// On the first save that modifies a <see cref="Transaction"/>, a separate
    /// context on the same connection commits a change to that row first — the
    /// hold or dispute that lands between the job's load and its save.
    /// </summary>
    private sealed class TransactionWriteRaceDbContext : AppDbContext
    {
        private readonly DbContextOptions<AppDbContext> _options;
        private bool _fired;

        public TransactionWriteRaceDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
            _options = options;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var modified = _fired
                ? null
                : ChangeTracker.Entries<Transaction>().FirstOrDefault(e => e.State == EntityState.Modified);
            if (modified is not null)
            {
                _fired = true;
                await using var other = new AppDbContext(_options);
                var row = await other.Set<Transaction>()
                    .SingleAsync(t => t.Id == modified.Entity.Id, cancellationToken);
                row.RowVersion = [1, 0, 0, 0, 0, 0, 0, 0];
                await other.SaveChangesAsync(cancellationToken);
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
