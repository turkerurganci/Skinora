using Microsoft.EntityFrameworkCore;
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
/// Integration coverage for <see cref="TimeoutSchedulingService"/> (T47).
/// Asserts that the per-tx Hangfire jobs are scheduled with the correct delay
/// and persisted onto the entity, that <c>CancelTimeoutJobsAsync</c> deletes
/// both jobs and that <c>ReschedulePaymentTimeoutAsync</c> resets the
/// <c>TimeoutRemainingSeconds</c> source-of-truth field (06 §8.1, 05 §4.4).
/// </summary>
public class TimeoutSchedulingServiceTests : IntegrationTestBase
{
    static TimeoutSchedulingServiceTests()
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
    public async Task SchedulePaymentTimeout_Schedules_Both_Payment_And_Warning_Jobs()
    {
        await TimeoutTestFixtures.ConfigureSettingAsync(
            Context, TimeoutSchedulingService.WarningRatioKey, "0.75");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var paymentDeadline = nowUtc.AddMinutes(60);
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: paymentDeadline,
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        var result = await sut.SchedulePaymentTimeoutAsync(transaction.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.NotNull(result.PaymentTimeoutJobId);
        Assert.NotNull(result.TimeoutWarningJobId);
        Assert.Equal(2, _scheduler.ScheduledCalls.Count);

        // Payment timeout = full 60 minutes
        var payment = _scheduler.ScheduledCalls.Single(c => c.TargetType == typeof(ITimeoutExecutor));
        Assert.Equal(TimeSpan.FromMinutes(60), payment.Delay);

        // Warning = 0.75 × 60 minutes = 45 minutes
        var warning = _scheduler.ScheduledCalls.Single(c => c.TargetType == typeof(IWarningDispatcher));
        Assert.Equal(TimeSpan.FromMinutes(45), warning.Delay);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(result.PaymentTimeoutJobId, persisted.PaymentTimeoutJobId);
        Assert.Equal(result.TimeoutWarningJobId, persisted.TimeoutWarningJobId);
    }

    [Fact]
    public async Task SchedulePaymentTimeout_NoWarning_When_Ratio_Unconfigured()
    {
        // No SystemSetting configured for timeout_warning_ratio → only payment job.
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(30),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        var result = await sut.SchedulePaymentTimeoutAsync(transaction.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.NotNull(result.PaymentTimeoutJobId);
        Assert.Null(result.TimeoutWarningJobId);
        Assert.Single(_scheduler.ScheduledCalls);
        Assert.Equal(typeof(ITimeoutExecutor), _scheduler.ScheduledCalls[0].TargetType);
    }

    [Fact]
    public async Task SchedulePaymentTimeout_Throws_When_Status_Not_ITEM_ESCROWED()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.CREATED, nowUtc,
            acceptDeadline: nowUtc.AddMinutes(30));
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.SchedulePaymentTimeoutAsync(transaction.Id, CancellationToken.None));
    }

    [Fact]
    public async Task SchedulePaymentTimeout_Throws_When_PaymentDeadline_Null()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: null,
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.SchedulePaymentTimeoutAsync(transaction.Id, CancellationToken.None));
    }

    [Fact]
    public async Task CancelTimeoutJobs_Deletes_Both_Jobs_And_Nulls_Ids()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(30),
            paymentTimeoutJobId: "payment-old",
            timeoutWarningJobId: "warning-old",
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        await sut.CancelTimeoutJobsAsync(transaction.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Contains("payment-old", _scheduler.DeletedJobIds);
        Assert.Contains("warning-old", _scheduler.DeletedJobIds);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(persisted.PaymentTimeoutJobId);
        Assert.Null(persisted.TimeoutWarningJobId);
        Assert.Null(persisted.TimeoutWarningSentAt);
    }

    [Fact]
    public async Task CancelTimeoutJobs_Idempotent_When_No_Jobs_Stored()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.CREATED, nowUtc, acceptDeadline: nowUtc.AddMinutes(15));
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        await sut.CancelTimeoutJobsAsync(transaction.Id, CancellationToken.None);

        Assert.Empty(_scheduler.DeletedJobIds);
    }

    [Fact]
    public async Task ReschedulePaymentTimeout_Deletes_Old_Issues_New_And_Sets_RemainingSeconds()
    {
        await TimeoutTestFixtures.ConfigureSettingAsync(
            Context, TimeoutSchedulingService.WarningRatioKey, "0.5");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(10),
            paymentTimeoutJobId: "payment-old",
            timeoutWarningJobId: "warning-old",
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var newRemaining = TimeSpan.FromMinutes(40);
        var newDeadline = nowUtc + newRemaining;

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        var result = await sut.ReschedulePaymentTimeoutAsync(
            transaction.Id, newRemaining, newDeadline, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Contains("payment-old", _scheduler.DeletedJobIds);
        Assert.Contains("warning-old", _scheduler.DeletedJobIds);
        Assert.NotNull(result.PaymentTimeoutJobId);
        Assert.NotNull(result.TimeoutWarningJobId);

        var payment = _scheduler.ScheduledCalls.Single(c => c.TargetType == typeof(ITimeoutExecutor));
        Assert.Equal(TimeSpan.FromMinutes(40), payment.Delay);
        var warning = _scheduler.ScheduledCalls.Single(c => c.TargetType == typeof(IWarningDispatcher));
        Assert.Equal(TimeSpan.FromMinutes(20), warning.Delay);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(newDeadline, persisted.PaymentDeadline);
        // CK_Transactions_FreezePassive — when TimeoutFrozenAt is NULL,
        // TimeoutRemainingSeconds must also be NULL (T50 owns the freeze/resume
        // lifecycle that consumes this field).
        Assert.Null(persisted.TimeoutRemainingSeconds);
    }

    [Fact]
    public async Task ReschedulePaymentTimeout_Skips_Warning_If_Already_Sent()
    {
        await TimeoutTestFixtures.ConfigureSettingAsync(
            Context, TimeoutSchedulingService.WarningRatioKey, "0.5");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(5),
            paymentTimeoutJobId: "payment-old",
            timeoutWarningJobId: "warning-old",
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.TimeoutWarningSentAt = nowUtc.AddMinutes(-5);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        var result = await sut.ReschedulePaymentTimeoutAsync(
            transaction.Id, TimeSpan.FromMinutes(20), nowUtc.AddMinutes(20), CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.NotNull(result.PaymentTimeoutJobId);
        Assert.Null(result.TimeoutWarningJobId);
        Assert.DoesNotContain(_scheduler.ScheduledCalls, c => c.TargetType == typeof(IWarningDispatcher));

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(persisted.TimeoutWarningJobId);
        Assert.NotNull(persisted.TimeoutWarningSentAt); // preserved
    }
}
