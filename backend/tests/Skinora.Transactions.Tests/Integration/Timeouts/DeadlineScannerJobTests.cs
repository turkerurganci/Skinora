using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
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
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, persisted.Status);
    }

    /// <summary>
    /// T124 gate — an overdue <c>DeliveryDeadline</c> is NOT consumed until
    /// T127 adds the verification round that 05 §4.4 / 03 §4.4 require before a
    /// delivery timeout may cancel. Cancelling here would refund the buyer and
    /// blame a seller who may have delivered without being confirmed.
    /// </summary>
    /// <remarks>
    /// The transaction must also stay SCANNABLE: nothing is stamped or
    /// consumed, so a second pass still sees the very same row — which is what
    /// lets T127 pick up transactions that expired before it shipped. The
    /// rescan below is the assertion for that, not a repetition.
    /// </remarks>
    [Fact]
    public async Task Scanner_Does_Not_Consume_Overdue_PAYMENT_RECEIVED_Until_T127()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
            deliveryDeadline: nowUtc.AddMinutes(-1),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();
        await sut.ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Null(persisted.CancelledAt);
        Assert.Null(persisted.CancelledBy);
        // Untouched, so the row is still exactly what T127 will inherit.
        Assert.Equal(nowUtc.AddMinutes(-1), persisted.DeliveryDeadline);
    }

    /// <summary>
    /// The gated delivery rows must not eat the batch. They are permanently
    /// overdue until T127, so sharing <c>DeadlineScannerBatchSize</c> with them
    /// would let a handful of stuck transactions silently stop every accept /
    /// seller-confirm / payment timeout in the system.
    /// </summary>
    [Fact]
    public async Task Scanner_Still_Consumes_Other_Phases_When_Gated_Delivery_Rows_Fill_The_Batch()
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
            TimeoutTestFixtures.Options(batchSize: 1),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        var persistedAccept = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == accept.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, persistedAccept.Status);

        var stillPending = await Context.Set<Transaction>().AsNoTracking()
            .CountAsync(t => t.Status == TransactionStatus.PAYMENT_RECEIVED);
        Assert.Equal(3, stillPending);
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
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
    }
}
