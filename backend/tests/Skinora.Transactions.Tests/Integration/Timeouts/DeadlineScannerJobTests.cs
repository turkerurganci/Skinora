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
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, persisted.Status);
        Assert.Equal(CancelledByType.TIMEOUT, persisted.CancelledBy);
    }

    [Fact]
    public async Task Scanner_Fires_Timeout_On_Overdue_TRADE_OFFER_SENT_TO_SELLER()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, nowUtc,
            tradeOfferToSellerDeadline: nowUtc.AddMinutes(-1),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, persisted.Status);
    }

    [Fact]
    public async Task Scanner_Fires_Timeout_On_Overdue_TRADE_OFFER_SENT_TO_BUYER()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.TRADE_OFFER_SENT_TO_BUYER, nowUtc,
            tradeOfferToBuyerDeadline: nowUtc.AddMinutes(-1),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new DeadlineScannerJob(
            Context, _scheduler, _clock,
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, persisted.Status);
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
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);
        await sut.ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
    }
}
