using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Platform.Domain.Entities;
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
            _seller.Id, TransactionStatus.SELLER_CONFIRMED, nowUtc,
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
        // WP12 — the seed now ships timeout_warning_ratio configured (0.75), so
        // explicitly unconfigure it to exercise the "no ratio → only payment
        // job" branch (an admin may clear the value).
        var ratioRow = await Context.Set<SystemSetting>()
            .SingleAsync(s => s.Key == TimeoutSchedulingService.WarningRatioKey);
        ratioRow.IsConfigured = false;
        ratioRow.Value = null;
        await Context.SaveChangesAsync();

        // No SystemSetting configured for timeout_warning_ratio → only payment job.
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.SELLER_CONFIRMED, nowUtc,
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
    public async Task SchedulePaymentTimeout_Throws_When_Status_Not_SELLER_CONFIRMED()
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
            _seller.Id, TransactionStatus.SELLER_CONFIRMED, nowUtc,
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
            _seller.Id, TransactionStatus.SELLER_CONFIRMED, nowUtc,
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
            _seller.Id, TransactionStatus.SELLER_CONFIRMED, nowUtc,
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
            _seller.Id, TransactionStatus.SELLER_CONFIRMED, nowUtc,
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

    // ─── T124 — delivery deadline arming ────────────────────────────────

    [Fact]
    public async Task ArmDeliveryDeadline_Writes_Deadline_From_Configured_Setting()
    {
        await TimeoutTestFixtures.ConfigureSettingAsync(
            Context, TimeoutSchedulingService.DeliveryTimeoutKey, "90", dataType: "int");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = await SeedPaymentReceivedAsync(nowUtc);

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        var deadline = await sut.ArmDeliveryDeadlineAsync(transaction.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(nowUtc.AddMinutes(90), deadline);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(nowUtc.AddMinutes(90), persisted.DeliveryDeadline);
    }

    [Fact]
    public async Task ArmDeliveryDeadline_Schedules_No_Hangfire_Job()
    {
        // 05 §4.4 "Aşama ayrımı" — the delivery phase is scanner-driven. A
        // delayed job here would give the phase a second, independent executor.
        await TimeoutTestFixtures.ConfigureSettingAsync(
            Context, TimeoutSchedulingService.DeliveryTimeoutKey, "90", dataType: "int");

        var transaction = await SeedPaymentReceivedAsync(_clock.GetUtcNow().UtcDateTime);

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        await sut.ArmDeliveryDeadlineAsync(transaction.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Empty(_scheduler.ScheduledCalls);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(persisted.PaymentTimeoutJobId);
        Assert.Null(persisted.TimeoutWarningJobId);
    }

    [Theory]
    [InlineData(null)]   // seed row, still unconfigured
    [InlineData("0")]    // admin/env validator rejects this, but a DB edit could not
    [InlineData("-15")]
    [InlineData("not-a-number")]
    public async Task ArmDeliveryDeadline_Falls_Back_When_Setting_Unusable(string? rawValue)
    {
        // A zero or negative window would arm the deadline in the past and put
        // the seller overdue the instant the payment lands, so anything
        // unusable falls back to the documented conservative default.
        if (rawValue is not null)
        {
            await TimeoutTestFixtures.ConfigureSettingAsync(
                Context, TimeoutSchedulingService.DeliveryTimeoutKey, rawValue, dataType: "int");
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = await SeedPaymentReceivedAsync(nowUtc);

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        var deadline = await sut.ArmDeliveryDeadlineAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(
            nowUtc.AddMinutes(TimeoutSchedulingService.DefaultDeliveryTimeoutMinutes),
            deadline);
        Assert.True(deadline > nowUtc, "The fallback must never arm a deadline in the past.");
    }

    [Fact]
    public async Task ArmDeliveryDeadline_Rejects_Non_PaymentReceived_State()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.SELLER_CONFIRMED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(30),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ArmDeliveryDeadlineAsync(transaction.Id, CancellationToken.None));

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(persisted.DeliveryDeadline);
    }

    // ─── T127 — freeze/resume phase shift ───────────────────────────────

    /// <summary>
    /// A transaction can reach PAYMENT_RECEIVED while still frozen: the state
    /// machine guards <c>ConfirmPayment</c> on <c>IsOnHold</c> only, so a
    /// maintenance freeze does not stop an on-chain payment from landing. The
    /// remainder captured at freeze belongs to the PAYMENT window, and
    /// <c>ResumeAsync</c> distributes it against whatever state it finds — so
    /// arming the delivery window has to re-capture it, or the seller inherits
    /// the seconds left on somebody else's clock.
    /// </summary>
    [Fact]
    public async Task ArmDeliveryDeadline_Recaptures_The_Remainder_When_Still_Frozen()
    {
        await TimeoutTestFixtures.ConfigureSettingAsync(
            Context, TimeoutSchedulingService.DeliveryTimeoutKey, "120", dataType: "int");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = await SeedPaymentReceivedAsync(nowUtc);
        // The state a freeze taken in SELLER_CONFIRMED leaves behind: three
        // minutes were left on the PAYMENT deadline.
        transaction.TimeoutFrozenAt = nowUtc.AddMinutes(-5);
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.MAINTENANCE;
        transaction.TimeoutRemainingSeconds = 180;
        await Context.SaveChangesAsync();

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        await sut.ArmDeliveryDeadlineAsync(transaction.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(120 * 60, persisted.TimeoutRemainingSeconds);
        // CK_Transactions_FreezeActive — overwritten, never cleared, while the
        // transaction is still frozen.
        Assert.Equal(nowUtc.AddMinutes(-5), persisted.TimeoutFrozenAt);
    }

    /// <summary>
    /// The unfrozen path is untouched: CK_Transactions_FreezePassive requires
    /// <c>TimeoutRemainingSeconds</c> to stay NULL while
    /// <c>TimeoutFrozenAt</c> is NULL, so arming must not invent a remainder.
    /// </summary>
    [Fact]
    public async Task ArmDeliveryDeadline_Leaves_The_Remainder_Null_When_Not_Frozen()
    {
        await TimeoutTestFixtures.ConfigureSettingAsync(
            Context, TimeoutSchedulingService.DeliveryTimeoutKey, "120", dataType: "int");

        var transaction = await SeedPaymentReceivedAsync(_clock.GetUtcNow().UtcDateTime);

        var sut = new TimeoutSchedulingService(Context, _scheduler, _clock);
        await sut.ArmDeliveryDeadlineAsync(transaction.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(persisted.TimeoutRemainingSeconds);
        Assert.Null(persisted.TimeoutFrozenAt);
    }

    private async Task<Transaction> SeedPaymentReceivedAsync(DateTime nowUtc)
    {
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.PaymentReceivedAt = nowUtc;
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();
        return transaction;
    }
}
