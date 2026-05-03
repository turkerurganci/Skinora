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
/// Integration coverage for <see cref="TimeoutFreezeService"/> (T50). Exercises
/// the full freeze → save → resume → save cycle against the shared SQL Server
/// fixture, mirrors the deadline / Hangfire-job invariants spelled out in 02
/// §3.3, 05 §4.4–§4.5 and 09 §13.3.
/// </summary>
public class TimeoutFreezeServiceTests : IntegrationTestBase
{
    static TimeoutFreezeServiceTests()
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

    private TimeoutFreezeService CreateSut()
    {
        var scheduling = new TimeoutSchedulingService(Context, _scheduler, _clock);
        return new TimeoutFreezeService(Context, _scheduler, scheduling, _clock);
    }

    // -------------------- FreezeAsync (per-tx) --------------------

    [Fact]
    public async Task FreezeAsync_ITEM_ESCROWED_Stamps_Fields_And_Cancels_Hangfire_Jobs()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(45),
            paymentTimeoutJobId: "payment-existing",
            timeoutWarningJobId: "warning-existing",
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = CreateSut();
        await sut.FreezeAsync(transaction, TimeoutFreezeReason.MAINTENANCE, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Contains("payment-existing", _scheduler.DeletedJobIds);
        Assert.Contains("warning-existing", _scheduler.DeletedJobIds);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(nowUtc, persisted.TimeoutFrozenAt);
        Assert.Equal(TimeoutFreezeReason.MAINTENANCE, persisted.TimeoutFreezeReason);
        Assert.Equal(45 * 60, persisted.TimeoutRemainingSeconds);
        Assert.Null(persisted.PaymentTimeoutJobId);
        Assert.Null(persisted.TimeoutWarningJobId);
    }

    [Fact]
    public async Task FreezeAsync_NonPayment_State_Captures_RemainingSeconds_From_Active_Deadline()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, nowUtc,
            tradeOfferToSellerDeadline: nowUtc.AddMinutes(120),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = CreateSut();
        await sut.FreezeAsync(transaction, TimeoutFreezeReason.STEAM_OUTAGE, CancellationToken.None);
        await Context.SaveChangesAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(nowUtc, persisted.TimeoutFrozenAt);
        Assert.Equal(TimeoutFreezeReason.STEAM_OUTAGE, persisted.TimeoutFreezeReason);
        // CK_Transactions_FreezeActive (06 §3.5): TimeoutRemainingSeconds NOT NULL
        // when frozen — captured from the state's active deadline per 06 §3.5
        // matrix (TRADE_OFFER_SENT_TO_SELLER → TradeOfferToSellerDeadline).
        Assert.Equal(120 * 60, persisted.TimeoutRemainingSeconds);
        // Original deadline preserved at freeze time — DeadlineScannerJob filters
        // TimeoutFrozenAt != null so the deadline does not need to move forward
        // until resume rewrites it from the remainder.
        Assert.Equal(nowUtc.AddMinutes(120), persisted.TradeOfferToSellerDeadline);
    }

    [Fact]
    public async Task FreezeAsync_Already_Frozen_Preserves_Original_Stamp_And_Reason()
    {
        var freezeStartUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, freezeStartUtc,
            paymentDeadline: freezeStartUtc.AddMinutes(30),
            timeoutFrozenAt: freezeStartUtc,
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.STEAM_OUTAGE;
        transaction.TimeoutRemainingSeconds = 1800;
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        // Time advances; second freeze attempt with a different reason fires.
        _clock.Advance(TimeSpan.FromMinutes(10));
        var sut = CreateSut();
        await sut.FreezeAsync(transaction, TimeoutFreezeReason.MAINTENANCE, CancellationToken.None);
        await Context.SaveChangesAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(freezeStartUtc, persisted.TimeoutFrozenAt);
        Assert.Equal(TimeoutFreezeReason.STEAM_OUTAGE, persisted.TimeoutFreezeReason);
        Assert.Equal(1800, persisted.TimeoutRemainingSeconds);
    }

    [Fact]
    public async Task FreezeAsync_ITEM_ESCROWED_PastDeadline_Captures_Zero_Seconds()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(-5),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = CreateSut();
        await sut.FreezeAsync(transaction, TimeoutFreezeReason.BLOCKCHAIN_DEGRADATION, CancellationToken.None);
        await Context.SaveChangesAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(0, persisted.TimeoutRemainingSeconds);
    }

    // -------------------- ResumeAsync (per-tx) --------------------

    [Fact]
    public async Task ResumeAsync_ITEM_ESCROWED_Reissues_Job_And_Clears_Freeze_Fields()
    {
        await TimeoutTestFixtures.ConfigureSettingAsync(
            Context, TimeoutSchedulingService.WarningRatioKey, "0.5");

        var freezeStartUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, freezeStartUtc,
            paymentDeadline: freezeStartUtc.AddMinutes(20),
            timeoutFrozenAt: freezeStartUtc,
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.MAINTENANCE;
        transaction.TimeoutRemainingSeconds = 20 * 60;
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        // Freeze lasts 15 minutes.
        _clock.Advance(TimeSpan.FromMinutes(15));
        var resumeNowUtc = _clock.GetUtcNow().UtcDateTime;

        var sut = CreateSut();
        await sut.ResumeAsync(transaction, CancellationToken.None);
        await Context.SaveChangesAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(persisted.TimeoutFrozenAt);
        Assert.Null(persisted.TimeoutFreezeReason);
        Assert.Null(persisted.TimeoutRemainingSeconds);
        // Reschedule authority: PaymentDeadline = now + remaining (20 min).
        Assert.Equal(resumeNowUtc.AddMinutes(20), persisted.PaymentDeadline);
        Assert.NotNull(persisted.PaymentTimeoutJobId);
        Assert.NotNull(persisted.TimeoutWarningJobId);

        var payment = _scheduler.ScheduledCalls.Single(c => c.TargetType == typeof(ITimeoutExecutor));
        Assert.Equal(TimeSpan.FromMinutes(20), payment.Delay);
        var warning = _scheduler.ScheduledCalls.Single(c => c.TargetType == typeof(IWarningDispatcher));
        Assert.Equal(TimeSpan.FromMinutes(10), warning.Delay);
    }

    [Fact]
    public async Task ResumeAsync_NonPayment_Bumps_Deadlines_By_Elapsed_Freeze()
    {
        var freezeStartUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, freezeStartUtc,
            tradeOfferToSellerDeadline: freezeStartUtc.AddMinutes(60),
            timeoutFrozenAt: freezeStartUtc,
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id);
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.STEAM_OUTAGE;
        transaction.TimeoutRemainingSeconds = 60 * 60;
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        _clock.Advance(TimeSpan.FromMinutes(7));

        var sut = CreateSut();
        await sut.ResumeAsync(transaction, CancellationToken.None);
        await Context.SaveChangesAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(persisted.TimeoutFrozenAt);
        Assert.Null(persisted.TimeoutFreezeReason);
        Assert.Equal(freezeStartUtc.AddMinutes(67), persisted.TradeOfferToSellerDeadline);
        // No Hangfire job for poller-based phases.
        Assert.Empty(_scheduler.ScheduledCalls);
    }

    [Fact]
    public async Task ResumeAsync_Not_Frozen_Is_NoOp()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(30),
            buyerId: (await TimeoutTestFixtures.AddBuyerAsync(Context)).Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = CreateSut();
        await sut.ResumeAsync(transaction, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Empty(_scheduler.ScheduledCalls);
        Assert.Empty(_scheduler.DeletedJobIds);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(nowUtc.AddMinutes(30), persisted.PaymentDeadline);
    }

    // -------------------- FreezeManyAsync --------------------

    [Fact]
    public async Task FreezeManyAsync_MAINTENANCE_Freezes_All_Eight_Active_States()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyer = await TimeoutTestFixtures.AddBuyerAsync(Context);

        var activeStatuses = new[]
        {
            TransactionStatus.CREATED,
            TransactionStatus.ACCEPTED,
            TransactionStatus.TRADE_OFFER_SENT_TO_SELLER,
            TransactionStatus.ITEM_ESCROWED,
            TransactionStatus.PAYMENT_RECEIVED,
            TransactionStatus.TRADE_OFFER_SENT_TO_BUYER,
            TransactionStatus.ITEM_DELIVERED,
            TransactionStatus.FLAGGED,
        };
        var allRows = activeStatuses
            .Select(s => TimeoutTestFixtures.NewTransaction(
                _seller.Id, s, nowUtc,
                paymentDeadline: s == TransactionStatus.ITEM_ESCROWED ? nowUtc.AddMinutes(30) : null,
                buyerId: s == TransactionStatus.CREATED ? null : buyer.Id,
                buyerRefundAddress: s == TransactionStatus.CREATED ? null : TimeoutTestFixtures.ValidWallet))
            .ToList();
        // Plus a terminal state to confirm it is excluded.
        var terminal = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.COMPLETED, nowUtc,
            buyerId: buyer.Id, buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().AddRange(allRows);
        Context.Set<Transaction>().Add(terminal);
        await Context.SaveChangesAsync();

        var sut = CreateSut();
        var affected = await sut.FreezeManyAsync(TimeoutFreezeReason.MAINTENANCE, CancellationToken.None);

        Assert.Equal(8, affected);

        var frozen = await Context.Set<Transaction>().AsNoTracking()
            .Where(t => t.TimeoutFrozenAt != null).ToListAsync();
        Assert.Equal(8, frozen.Count);
        Assert.All(frozen, t => Assert.Equal(TimeoutFreezeReason.MAINTENANCE, t.TimeoutFreezeReason));

        var persistedTerminal = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == terminal.Id);
        Assert.Null(persistedTerminal.TimeoutFrozenAt);
    }

    [Fact]
    public async Task FreezeManyAsync_STEAM_OUTAGE_Targets_Only_Trade_Offer_States()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyer = await TimeoutTestFixtures.AddBuyerAsync(Context);

        var steamSeller = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, nowUtc,
            tradeOfferToSellerDeadline: nowUtc.AddMinutes(60),
            buyerId: buyer.Id);
        var steamBuyer = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.TRADE_OFFER_SENT_TO_BUYER, nowUtc,
            tradeOfferToBuyerDeadline: nowUtc.AddMinutes(60),
            buyerId: buyer.Id, buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        var payment = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(30),
            buyerId: buyer.Id, buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        var created = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.CREATED, nowUtc,
            acceptDeadline: nowUtc.AddMinutes(15));
        Context.Set<Transaction>().AddRange(steamSeller, steamBuyer, payment, created);
        await Context.SaveChangesAsync();

        var sut = CreateSut();
        var affected = await sut.FreezeManyAsync(TimeoutFreezeReason.STEAM_OUTAGE, CancellationToken.None);

        Assert.Equal(2, affected);
        var persistedSeller = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == steamSeller.Id);
        var persistedBuyer = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == steamBuyer.Id);
        var persistedPayment = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == payment.Id);
        var persistedCreated = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == created.Id);
        Assert.Equal(TimeoutFreezeReason.STEAM_OUTAGE, persistedSeller.TimeoutFreezeReason);
        Assert.Equal(TimeoutFreezeReason.STEAM_OUTAGE, persistedBuyer.TimeoutFreezeReason);
        Assert.Null(persistedPayment.TimeoutFrozenAt);
        Assert.Null(persistedCreated.TimeoutFrozenAt);
    }

    [Fact]
    public async Task FreezeManyAsync_BLOCKCHAIN_DEGRADATION_Targets_Only_ITEM_ESCROWED()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyer = await TimeoutTestFixtures.AddBuyerAsync(Context);

        var payment = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(45),
            paymentTimeoutJobId: "p-old",
            timeoutWarningJobId: "w-old",
            buyerId: buyer.Id, buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        var steam = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, nowUtc,
            tradeOfferToSellerDeadline: nowUtc.AddMinutes(60), buyerId: buyer.Id);
        Context.Set<Transaction>().AddRange(payment, steam);
        await Context.SaveChangesAsync();

        var sut = CreateSut();
        var affected = await sut.FreezeManyAsync(TimeoutFreezeReason.BLOCKCHAIN_DEGRADATION, CancellationToken.None);

        Assert.Equal(1, affected);
        Assert.Contains("p-old", _scheduler.DeletedJobIds);
        Assert.Contains("w-old", _scheduler.DeletedJobIds);

        var persistedPayment = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == payment.Id);
        Assert.Equal(TimeoutFreezeReason.BLOCKCHAIN_DEGRADATION, persistedPayment.TimeoutFreezeReason);
        Assert.Equal(45 * 60, persistedPayment.TimeoutRemainingSeconds);
    }

    [Fact]
    public async Task FreezeManyAsync_Skips_Already_Held_And_Already_Frozen()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyer = await TimeoutTestFixtures.AddBuyerAsync(Context);

        var emergency = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(30),
            isOnHold: true,
            timeoutFrozenAt: nowUtc.AddMinutes(-5),
            buyerId: buyer.Id, buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        emergency.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        emergency.TimeoutRemainingSeconds = 35 * 60;
        emergency.EmergencyHoldAt = nowUtc.AddMinutes(-5);
        emergency.EmergencyHoldReason = "Sanctions match";
        emergency.EmergencyHoldByAdminId = _seller.Id;
        var steamFrozen = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, nowUtc,
            tradeOfferToSellerDeadline: nowUtc.AddMinutes(60),
            timeoutFrozenAt: nowUtc.AddMinutes(-2),
            buyerId: buyer.Id);
        steamFrozen.TimeoutFreezeReason = TimeoutFreezeReason.STEAM_OUTAGE;
        steamFrozen.TimeoutRemainingSeconds = 60 * 60;
        Context.Set<Transaction>().AddRange(emergency, steamFrozen);
        await Context.SaveChangesAsync();

        var sut = CreateSut();
        var affected = await sut.FreezeManyAsync(TimeoutFreezeReason.MAINTENANCE, CancellationToken.None);

        Assert.Equal(0, affected);
        var persistedEmergency = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == emergency.Id);
        Assert.Equal(TimeoutFreezeReason.EMERGENCY_HOLD, persistedEmergency.TimeoutFreezeReason);
        var persistedSteam = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == steamFrozen.Id);
        Assert.Equal(TimeoutFreezeReason.STEAM_OUTAGE, persistedSteam.TimeoutFreezeReason);
    }

    [Fact]
    public async Task FreezeManyAsync_EMERGENCY_HOLD_Throws()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.FreezeManyAsync(TimeoutFreezeReason.EMERGENCY_HOLD, CancellationToken.None));
    }

    [Fact]
    public async Task FreezeManyAsync_NoMatches_Returns_Zero()
    {
        var sut = CreateSut();
        var affected = await sut.FreezeManyAsync(TimeoutFreezeReason.MAINTENANCE, CancellationToken.None);
        Assert.Equal(0, affected);
    }

    // -------------------- ResumeManyAsync --------------------

    [Fact]
    public async Task ResumeManyAsync_Resumes_Only_Matching_Reason()
    {
        await TimeoutTestFixtures.ConfigureSettingAsync(
            Context, TimeoutSchedulingService.WarningRatioKey, "0.5");

        var freezeStartUtc = _clock.GetUtcNow().UtcDateTime;
        var buyer = await TimeoutTestFixtures.AddBuyerAsync(Context);

        var maintFrozen = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, freezeStartUtc,
            paymentDeadline: freezeStartUtc.AddMinutes(40),
            timeoutFrozenAt: freezeStartUtc,
            buyerId: buyer.Id, buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        maintFrozen.TimeoutFreezeReason = TimeoutFreezeReason.MAINTENANCE;
        maintFrozen.TimeoutRemainingSeconds = 40 * 60;

        var steamFrozen = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, freezeStartUtc,
            tradeOfferToSellerDeadline: freezeStartUtc.AddMinutes(60),
            timeoutFrozenAt: freezeStartUtc, buyerId: buyer.Id);
        steamFrozen.TimeoutFreezeReason = TimeoutFreezeReason.STEAM_OUTAGE;
        steamFrozen.TimeoutRemainingSeconds = 60 * 60;

        Context.Set<Transaction>().AddRange(maintFrozen, steamFrozen);
        await Context.SaveChangesAsync();

        _clock.Advance(TimeSpan.FromMinutes(10));
        var resumeNowUtc = _clock.GetUtcNow().UtcDateTime;

        var sut = CreateSut();
        var affected = await sut.ResumeManyAsync(TimeoutFreezeReason.MAINTENANCE, CancellationToken.None);

        Assert.Equal(1, affected);
        var persistedMaint = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == maintFrozen.Id);
        Assert.Null(persistedMaint.TimeoutFrozenAt);
        Assert.Equal(resumeNowUtc.AddMinutes(40), persistedMaint.PaymentDeadline);
        Assert.NotNull(persistedMaint.PaymentTimeoutJobId);

        var persistedSteam = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == steamFrozen.Id);
        Assert.Equal(TimeoutFreezeReason.STEAM_OUTAGE, persistedSteam.TimeoutFreezeReason);
        Assert.Equal(freezeStartUtc, persistedSteam.TimeoutFrozenAt);
    }

    [Fact]
    public async Task ResumeManyAsync_EMERGENCY_HOLD_Throws()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ResumeManyAsync(TimeoutFreezeReason.EMERGENCY_HOLD, CancellationToken.None));
    }

    [Fact]
    public async Task FreezeMany_Then_ResumeMany_Restores_Original_Remaining_Time()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyer = await TimeoutTestFixtures.AddBuyerAsync(Context);

        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(50),
            paymentTimeoutJobId: "p-original",
            timeoutWarningJobId: "w-original",
            buyerId: buyer.Id, buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = CreateSut();

        // Freeze at t=0, deadline was now+50.
        await sut.FreezeManyAsync(TimeoutFreezeReason.BLOCKCHAIN_DEGRADATION, CancellationToken.None);

        // Wait 17 minutes, resume.
        _clock.Advance(TimeSpan.FromMinutes(17));
        var resumeNowUtc = _clock.GetUtcNow().UtcDateTime;
        var affected = await sut.ResumeManyAsync(TimeoutFreezeReason.BLOCKCHAIN_DEGRADATION, CancellationToken.None);

        Assert.Equal(1, affected);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        // The user had 50 minutes left at freeze time; after resume they still
        // have 50 minutes from "now" (freeze time was excluded from countdown).
        Assert.Equal(resumeNowUtc.AddMinutes(50), persisted.PaymentDeadline);
        Assert.Null(persisted.TimeoutFrozenAt);
        Assert.Null(persisted.TimeoutFreezeReason);
        Assert.Null(persisted.TimeoutRemainingSeconds);
    }
}
