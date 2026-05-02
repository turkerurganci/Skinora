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
/// Integration coverage for <see cref="TimeoutExecutor"/> + <see cref="StubWarningDispatcher"/>
/// (T47, 09 §13.3 no-op pattern). The executor must transition to
/// <c>CANCELLED_TIMEOUT</c> only when state, hold, freeze and deadline all
/// align; every other path is a silent no-op so orphan/stale Hangfire jobs
/// cannot push a transaction off its track.
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
    public async Task ExecutePaymentTimeout_Transitions_To_CANCELLED_TIMEOUT_When_Overdue()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(-1),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.EscrowBotAssetId = "100200300-bot";
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutExecutor(Context, _clock, NullLogger<TimeoutExecutor>.Instance);
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
        transaction.EscrowBotAssetId = "100200300-bot";
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutExecutor(Context, _clock, NullLogger<TimeoutExecutor>.Instance);
        await sut.ExecutePaymentTimeoutAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
    }

    [Fact]
    public async Task ExecutePaymentTimeout_NoOp_When_Frozen()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(-1),
            timeoutFrozenAt: nowUtc.AddMinutes(-30),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.EscrowBotAssetId = "100200300-bot";
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.MAINTENANCE;
        transaction.TimeoutRemainingSeconds = 1800; // CK_Transactions_FreezeActive
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutExecutor(Context, _clock, NullLogger<TimeoutExecutor>.Instance);
        await sut.ExecutePaymentTimeoutAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, persisted.Status);
    }

    [Fact]
    public async Task ExecutePaymentTimeout_NoOp_When_OnHold()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(-1),
            isOnHold: true,
            timeoutFrozenAt: nowUtc.AddMinutes(-1),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.EscrowBotAssetId = "100200300-bot";
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        transaction.TimeoutRemainingSeconds = 1800; // CK_Transactions_FreezeActive
        // CK_Transactions_Hold — emergency-hold fields must accompany IsOnHold=true.
        transaction.EmergencyHoldAt = nowUtc.AddMinutes(-1);
        transaction.EmergencyHoldReason = "test";
        transaction.EmergencyHoldByAdminId = _seller.Id; // any User Id satisfies the FK
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutExecutor(Context, _clock, NullLogger<TimeoutExecutor>.Instance);
        await sut.ExecutePaymentTimeoutAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, persisted.Status);
    }

    [Fact]
    public async Task ExecutePaymentTimeout_NoOp_When_Deadline_In_Future()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(15),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.EscrowBotAssetId = "100200300-bot";
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutExecutor(Context, _clock, NullLogger<TimeoutExecutor>.Instance);
        await sut.ExecutePaymentTimeoutAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, persisted.Status);
    }

    [Fact]
    public async Task DispatchWarning_Stamps_TimeoutWarningSentAt()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(15),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.EscrowBotAssetId = "100200300-bot";
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new StubWarningDispatcher(Context, _clock, NullLogger<StubWarningDispatcher>.Instance);
        await sut.DispatchWarningAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.NotNull(persisted.TimeoutWarningSentAt);
    }

    [Fact]
    public async Task DispatchWarning_NoOp_When_Already_Sent()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var sentAt = nowUtc.AddMinutes(-10);
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(15),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.EscrowBotAssetId = "100200300-bot";
        transaction.TimeoutWarningSentAt = sentAt;
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new StubWarningDispatcher(Context, _clock, NullLogger<StubWarningDispatcher>.Instance);
        await sut.DispatchWarningAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(sentAt, persisted.TimeoutWarningSentAt);
    }
}
