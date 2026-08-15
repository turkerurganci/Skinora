using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Delivery;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Timeouts;

/// <summary>
/// Integration coverage for <see cref="DeadlineScannerJob"/> (T47, 05 §4.4
/// "Aşama ayrımı": scanner-driven for non-payment phases).
/// </summary>
public class DeadlineScannerJobTests : IntegrationTestBase
{
    static DeadlineScannerJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private FakeTimeProvider _clock = null!;
    private CapturingJobScheduler _scheduler = null!;
    private User _seller = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = TimeoutTestFixtures.SellerSteamId,
            SteamDisplayName = "Seller",
        };
        context.Set<User>().Add(_seller);
        await context.SaveChangesAsync();
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero));
        _scheduler = new CapturingJobScheduler();
    }

    /// <summary>
    /// T127 — scanner wired to a caller-supplied delivery round. What the round
    /// itself concludes from Steam is <see cref="Delivery.DeliveryTimeoutRoundTests"/>;
    /// here the question is only what the scanner does with each answer.
    /// </summary>
    private DeadlineScannerJob CreateSut(IDeliveryTimeoutRound round) =>
        new(Context, _scheduler, _clock,
            TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            round,
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);

    [Fact]
    public async Task Scanner_Fires_Timeout_On_Overdue_CREATED()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.CREATED, nowUtc,
            acceptDeadline: nowUtc.AddMinutes(-1));
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            TimeoutTestFixtures.NoOpDeliveryRound(),
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, persisted.Status);
        Assert.Equal(CancelledByType.TIMEOUT, persisted.CancelledBy);
    }

    [Fact]
    public async Task Scanner_Fires_Timeout_On_Overdue_ACCEPTED()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ACCEPTED, nowUtc,
            sellerConfirmDeadline: nowUtc.AddMinutes(-1),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            TimeoutTestFixtures.NoOpDeliveryRound(),
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, persisted.Status);
    }

    /// <summary>
    /// T127 — an overdue <c>DeliveryDeadline</c> no longer decides anything on
    /// its own: the scanner runs the 05 §4.4 verification round and does only
    /// what that round concludes. When it concludes nothing, the transaction
    /// stays exactly where it was.
    /// </summary>
    /// <remarks>
    /// The row must also stay SCANNABLE — nothing is stamped or consumed — so a
    /// later pass, once the platform can see again, still finds it. The rescan
    /// below is the assertion for that, not a repetition.
    /// </remarks>
    [Fact]
    public async Task Scanner_Holds_An_Overdue_Delivery_When_The_Round_Concludes_Nothing()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
            deliveryDeadline: nowUtc.AddMinutes(-1),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var round = TimeoutTestFixtures.NoOpDeliveryRound();
        var sut = CreateSut(round);
        await sut.ScanAndRescheduleAsync();
        await sut.ScanAndRescheduleAsync();

        // The round was consulted on both passes — the deadline is what makes
        // the row eligible, and only the round makes it terminal.
        Assert.Equal([transaction.Id, transaction.Id], round.Rounds);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Null(persisted.CancelledAt);
        Assert.Null(persisted.CancelledBy);
        Assert.Equal(nowUtc.AddMinutes(-1), persisted.DeliveryDeadline);
    }

    /// <summary>
    /// The other half: when the round does authorise a cancellation, the
    /// transaction joins the shared timeout path and is cancelled like any
    /// other phase (03 §4.4 steps 2–7).
    /// </summary>
    [Fact]
    public async Task Scanner_Cancels_An_Overdue_Delivery_When_The_Round_Authorises_It()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
            deliveryDeadline: nowUtc.AddMinutes(-1),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var round = TimeoutTestFixtures.NoOpDeliveryRound();
        round.Decision = DeliveryTimeoutDecision.Cancel;
        await CreateSut(round).ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, persisted.Status);
        Assert.Equal(CancelledByType.TIMEOUT, persisted.CancelledBy);

        // WP15 — the cancellation is attributed through the shared path, so the
        // audit row the reputation map reads exists (06 §3.1, §3.6).
        var history = await Context.Set<TransactionHistory>().AsNoTracking()
            .SingleAsync(h => h.TransactionId == transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, history.PreviousStatus);
        Assert.Equal(nameof(TransactionTrigger.Timeout), history.Trigger);
    }

    /// <summary>
    /// A round costs up to two rate-limited Steam reads (08 §2.2), so the
    /// delivery phase drains in deadline order under its own budget rather than
    /// saturating the sidecar queue on one pass.
    /// </summary>
    [Fact]
    public async Task Scanner_Caps_Delivery_Verification_Rounds_Per_Pass()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyerId = (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id;

        var oldest = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
            deliveryDeadline: nowUtc.AddMinutes(-30), buyerId: buyerId,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(oldest);
        for (var i = 0; i < 3; i++)
        {
            Context.Set<Transaction>().Add(TimeoutTestFixtures.NewTransaction(
                _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
                deliveryDeadline: nowUtc.AddMinutes(-1 - i), buyerId: buyerId,
                buyerRefundAddress: TimeoutTestFixtures.ValidWallet));
        }
        await Context.SaveChangesAsync();

        var round = TimeoutTestFixtures.NoOpDeliveryRound();
        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            round,
            TimeoutTestFixtures.Options(deliveryVerificationBatchSize: 1),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        // Exactly one round, and it went to the transaction that has been
        // waiting longest.
        Assert.Equal([oldest.Id], round.Rounds);
    }

    /// <summary>
    /// Held delivery rows must not eat the batch. Three of the five verdicts
    /// leave a row in PAYMENT_RECEIVED and permanently overdue, so sharing
    /// <c>DeadlineScannerBatchSize</c> with them would let a handful of stuck
    /// transactions silently stop every accept / seller-confirm / payment
    /// timeout in the system — the hazard T124 named, which survives T127.
    /// </summary>
    [Fact]
    public async Task Scanner_Still_Consumes_Other_Phases_When_Held_Delivery_Rows_Pile_Up()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyerId = (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id;

        for (var i = 0; i < 3; i++)
        {
            Context.Set<Transaction>().Add(TimeoutTestFixtures.NewTransaction(
                _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
                deliveryDeadline: nowUtc.AddMinutes(-10 - i),
                buyerId: buyerId,
                buyerRefundAddress: TimeoutTestFixtures.ValidWallet));
        }

        var accept = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.CREATED, nowUtc,
            acceptDeadline: nowUtc.AddMinutes(-1));
        Context.Set<Transaction>().Add(accept);
        await Context.SaveChangesAsync();

        // Batch of one: if the gated rows shared this query they would win the
        // single slot on every pass and the CREATED row would never time out.
        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            TimeoutTestFixtures.NoOpDeliveryRound(),
            TimeoutTestFixtures.Options(batchSize: 1),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        var persistedAccept = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == accept.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, persistedAccept.Status);

        var stillPending = await Context.Set<Transaction>().AsNoTracking()
            .CountAsync(t => t.Status == TransactionStatus.PAYMENT_RECEIVED);
        Assert.Equal(3, stillPending);
    }

    /// <summary>
    /// <b>Finding B2 — the other half of the same hazard.</b> Protecting the
    /// three self-deciding phases from the held rows (the test above) said
    /// nothing about protecting the DELIVERY phase from them.
    /// </summary>
    /// <remarks>
    /// Held rows never leave this query: no arm moves <c>DeliveryDeadline</c> or
    /// the status. Ordered by deadline they are the oldest, so they occupy the
    /// window permanently — and a delivery that expires afterwards is never
    /// examined at all. Ordering by "when did we last look", nulls first, is
    /// what makes that impossible.
    /// </remarks>
    [Fact]
    public async Task Scanner_Examines_A_Never_Rounded_Delivery_Before_Held_Ones()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyerId = (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id;

        // Three rows that have been held for hours — the oldest deadlines in the
        // system, and all examined moments ago.
        for (var i = 0; i < 3; i++)
        {
            var held = TimeoutTestFixtures.NewTransaction(
                _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
                deliveryDeadline: nowUtc.AddHours(-5 - i), buyerId: buyerId,
                buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
            held.DeliveryRoundAt = nowUtc.AddSeconds(-30);
            Context.Set<Transaction>().Add(held);
        }

        // And one delivery that just expired and has never had a round.
        var fresh = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
            deliveryDeadline: nowUtc.AddMinutes(-1), buyerId: buyerId,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(fresh);
        await Context.SaveChangesAsync();

        var round = TimeoutTestFixtures.NoOpDeliveryRound(_clock);
        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            round,
            TimeoutTestFixtures.Options(deliveryVerificationBatchSize: 1),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        // Under deadline ordering the five-hour-old held row would have taken
        // the only slot, on this pass and on every pass after it.
        Assert.Equal([fresh.Id], round.Rounds);
    }

    /// <summary>
    /// The rotation the fairness ordering buys: a backlog larger than the
    /// per-pass budget drains across passes instead of the head of the queue
    /// consuming it forever.
    /// </summary>
    [Fact]
    public async Task Scanner_Rotates_The_Delivery_Window_Across_Passes()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyerId = (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id;

        for (var i = 0; i < 3; i++)
        {
            Context.Set<Transaction>().Add(TimeoutTestFixtures.NewTransaction(
                _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
                deliveryDeadline: nowUtc.AddMinutes(-10 - i), buyerId: buyerId,
                buyerRefundAddress: TimeoutTestFixtures.ValidWallet));
        }
        await Context.SaveChangesAsync();

        var round = TimeoutTestFixtures.NoOpDeliveryRound(_clock);
        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            round,
            TimeoutTestFixtures.Options(deliveryVerificationBatchSize: 1),
            NullLogger<DeadlineScannerJob>.Instance);

        for (var pass = 0; pass < 3; pass++)
        {
            await sut.ScanAndRescheduleAsync();
            _clock.Advance(TimeSpan.FromSeconds(30));
        }

        // Three passes, three DISTINCT transactions. Before the fix all three
        // passes went to the same oldest row.
        Assert.Equal(3, round.Rounds.Distinct().Count());
    }

    /// <summary>
    /// The re-check interval throttles a held row without retiring it: 08 §2.3
    /// does not let the platform treat "cannot see" as settled, and an
    /// unreadable inventory can become readable.
    /// </summary>
    [Fact]
    public async Task Scanner_Re_Examines_A_Held_Delivery_Once_The_Recheck_Interval_Passes()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyerId = (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id;

        var due = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
            deliveryDeadline: nowUtc.AddHours(-2), buyerId: buyerId,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        due.DeliveryRoundAt = nowUtc.AddSeconds(-901);

        var tooRecent = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
            deliveryDeadline: nowUtc.AddHours(-3), buyerId: buyerId,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        tooRecent.DeliveryRoundAt = nowUtc.AddSeconds(-899);

        Context.Set<Transaction>().AddRange(due, tooRecent);
        await Context.SaveChangesAsync();

        var round = TimeoutTestFixtures.NoOpDeliveryRound(_clock);
        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            round,
            TimeoutTestFixtures.Options(deliveryRoundRecheckSeconds: 900),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        Assert.Equal([due.Id], round.Rounds);
    }

    [Fact]
    public async Task Scanner_Skips_Overdue_When_Frozen()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.CREATED, nowUtc,
            acceptDeadline: nowUtc.AddMinutes(-30),
            timeoutFrozenAt: nowUtc.AddMinutes(-25));
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.MAINTENANCE;
        transaction.TimeoutRemainingSeconds = 1800; // CK_Transactions_FreezeActive
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            TimeoutTestFixtures.NoOpDeliveryRound(),
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
    }

    [Fact]
    public async Task Scanner_Skips_Overdue_When_OnHold()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.CREATED, nowUtc,
            acceptDeadline: nowUtc.AddMinutes(-30),
            isOnHold: true,
            timeoutFrozenAt: nowUtc.AddMinutes(-30));
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        transaction.TimeoutRemainingSeconds = 1800; // CK_Transactions_FreezeActive
        // CK_Transactions_Hold — emergency-hold fields must accompany IsOnHold=true.
        transaction.EmergencyHoldAt = nowUtc.AddMinutes(-30);
        transaction.EmergencyHoldReason = "test";
        transaction.EmergencyHoldByAdminId = _seller.Id; // any User Id satisfies the FK
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            TimeoutTestFixtures.NoOpDeliveryRound(),
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
    }

    [Fact]
    public async Task Scanner_Reschedules_Itself()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        // Empty DB — scanner should still self-reschedule.
        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            TimeoutTestFixtures.NoOpDeliveryRound(),
            TimeoutTestFixtures.Options(scannerSeconds: 45),
            NullLogger<DeadlineScannerJob>.Instance);

        await sut.ScanAndRescheduleAsync();

        var rescheduled = Assert.Single(_scheduler.ScheduledCalls);
        Assert.Equal(typeof(IDeadlineScannerJob), rescheduled.TargetType);
        Assert.Equal(TimeSpan.FromSeconds(45), rescheduled.Delay);
    }

    [Fact]
    public async Task Scanner_Skips_Future_Deadlines()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.CREATED, nowUtc,
            acceptDeadline: nowUtc.AddMinutes(30));
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            TimeoutTestFixtures.NoOpDeliveryRound(),
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
    }
}
