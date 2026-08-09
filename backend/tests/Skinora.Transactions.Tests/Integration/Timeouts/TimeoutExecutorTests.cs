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
/// Integration coverage for <see cref="TimeoutExecutor"/> (T47, 09 §13.3
/// no-op pattern). The executor must transition to <c>CANCELLED_TIMEOUT</c>
/// only when state, hold, freeze and deadline all align; every other path is
/// a silent no-op so orphan/stale Hangfire jobs cannot push a transaction off
/// its track. Warning-dispatch coverage lives in
/// <see cref="WarningDispatcherTests"/> (T48).
/// </summary>
public class TimeoutExecutorTests : IntegrationTestBase
{
    static TimeoutExecutorTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private FakeTimeProvider _clock = null!;
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
    }

    [Fact]
    public async Task ExecutePaymentTimeout_Writes_History_And_Attributes_Reputation_To_Buyer()
    {
        // WP15 — the timeout attribution chain. The payment timeout fires from
        // ITEM_ESCROWED, which 06 §3.1 attributes to the BUYER. The aggregator
        // can only resolve that responsibility from the TransactionHistory
        // PreviousStatus this path now writes — without it the timeout would be
        // silently dropped from reputation (the bug WP15 closes).
        var buyer = await TimeoutTestFixtures.AddBuyerAsync(Context);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.SELLER_CONFIRMED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(-1),
            buyerId: buyer.Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutExecutor(
            Context, _clock, TimeoutTestFixtures.NoOpSideEffects(),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.RealReputationRefresher(Context),
            NullLogger<TimeoutExecutor>.Instance);
        await sut.ExecutePaymentTimeoutAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, persisted.Status);

        var history = await Context.Set<TransactionHistory>().AsNoTracking()
            .SingleAsync(h => h.TransactionId == transaction.Id);
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, history.PreviousStatus);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, history.NewStatus);
        Assert.Equal("Timeout", history.Trigger);
        Assert.Equal(ActorType.SYSTEM, history.ActorType);

        // Buyer is the at-fault party: 1 responsible cancel, 0 successes → 0.0.
        var reloadedBuyer = await Context.Set<User>().AsNoTracking().SingleAsync(u => u.Id == buyer.Id);
        Assert.Equal(0m, reloadedBuyer.SuccessfulTransactionRate);
        Assert.Equal(0, reloadedBuyer.CompletedTransactionCount);

        // Seller is not responsible for a payment timeout → unaffected (null rate).
        var reloadedSeller = await Context.Set<User>().AsNoTracking().SingleAsync(u => u.Id == _seller.Id);
        Assert.Null(reloadedSeller.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task ExecutePaymentTimeout_Transitions_To_CANCELLED_TIMEOUT_When_Overdue()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.SELLER_CONFIRMED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(-1),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutExecutor(Context, _clock, TimeoutTestFixtures.NoOpSideEffects(), TimeoutTestFixtures.NoOpPostCancelMonitor(), TimeoutTestFixtures.NoOpReputationRefresher(), NullLogger<TimeoutExecutor>.Instance);
        await sut.ExecutePaymentTimeoutAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, persisted.Status);
        Assert.Equal(CancelledByType.TIMEOUT, persisted.CancelledBy);
        Assert.NotNull(persisted.CancelledAt);
    }

    [Fact]
    public async Task ExecutePaymentTimeout_NoOp_When_State_Already_Advanced()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(-1),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutExecutor(Context, _clock, TimeoutTestFixtures.NoOpSideEffects(), TimeoutTestFixtures.NoOpPostCancelMonitor(), TimeoutTestFixtures.NoOpReputationRefresher(), NullLogger<TimeoutExecutor>.Instance);
        await sut.ExecutePaymentTimeoutAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
    }

    [Fact]
    public async Task ExecutePaymentTimeout_NoOp_When_Frozen()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.SELLER_CONFIRMED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(-1),
            timeoutFrozenAt: nowUtc.AddMinutes(-30),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.MAINTENANCE;
        transaction.TimeoutRemainingSeconds = 1800; // CK_Transactions_FreezeActive
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutExecutor(Context, _clock, TimeoutTestFixtures.NoOpSideEffects(), TimeoutTestFixtures.NoOpPostCancelMonitor(), TimeoutTestFixtures.NoOpReputationRefresher(), NullLogger<TimeoutExecutor>.Instance);
        await sut.ExecutePaymentTimeoutAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, persisted.Status);
    }

    [Fact]
    public async Task ExecutePaymentTimeout_NoOp_When_OnHold()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.SELLER_CONFIRMED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(-1),
            isOnHold: true,
            timeoutFrozenAt: nowUtc.AddMinutes(-1),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        transaction.TimeoutRemainingSeconds = 1800; // CK_Transactions_FreezeActive
        // CK_Transactions_Hold — emergency-hold fields must accompany IsOnHold=true.
        transaction.EmergencyHoldAt = nowUtc.AddMinutes(-1);
        transaction.EmergencyHoldReason = "test";
        transaction.EmergencyHoldByAdminId = _seller.Id; // any User Id satisfies the FK
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutExecutor(Context, _clock, TimeoutTestFixtures.NoOpSideEffects(), TimeoutTestFixtures.NoOpPostCancelMonitor(), TimeoutTestFixtures.NoOpReputationRefresher(), NullLogger<TimeoutExecutor>.Instance);
        await sut.ExecutePaymentTimeoutAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, persisted.Status);
    }

    [Fact]
    public async Task ExecutePaymentTimeout_NoOp_When_Deadline_In_Future()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.SELLER_CONFIRMED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(15),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutExecutor(Context, _clock, TimeoutTestFixtures.NoOpSideEffects(), TimeoutTestFixtures.NoOpPostCancelMonitor(), TimeoutTestFixtures.NoOpReputationRefresher(), NullLogger<TimeoutExecutor>.Instance);
        await sut.ExecutePaymentTimeoutAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, persisted.Status);
    }

}
