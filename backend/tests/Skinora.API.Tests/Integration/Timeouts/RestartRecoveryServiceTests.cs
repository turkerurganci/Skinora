using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Skinora.API.BackgroundJobs.Timeouts;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.API.Tests.Integration.Timeouts;

/// <summary>
/// Integration coverage for <see cref="RestartRecoveryService"/> (T47, 05 §4.4
/// "Restart sonrası recovery"). Exercises outage-window detection, deadline
/// extension across all four phases and the per-tx Hangfire reschedule for
/// ITEM_ESCROWED rows.
/// </summary>
public class RestartRecoveryServiceTests : IntegrationTestBase
{
    static RestartRecoveryServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string SellerSteamId = "76561198900000111";
    private const string ValidWallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";

    private FakeTimeProvider _clock = null!;
    private CapturingScheduler _scheduler = null!;
    private User _seller = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteamId,
            SteamDisplayName = "Seller",
        };
        context.Set<User>().Add(_seller);
        await context.SaveChangesAsync();

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero));
        _scheduler = new CapturingScheduler();
    }

    [Fact]
    public async Task Below_Threshold_Outage_Stamps_Heartbeat_And_Skips_Extension()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        await StampHeartbeatAsync(nowUtc.AddSeconds(-30));

        var transaction = NewActive(TransactionStatus.CREATED, acceptDeadline: nowUtc.AddMinutes(15));
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = BuildSut(recoveryThresholdSeconds: 60);
        var result = await sut.RunAsync(CancellationToken.None);

        Assert.False(result.ExtensionApplied);
        Assert.Equal(0, result.ExtendedTransactionCount);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(nowUtc.AddMinutes(15), persisted.AcceptDeadline);

        var heartbeat = await Context.Set<SystemHeartbeat>().AsNoTracking().SingleAsync();
        Assert.Equal(nowUtc, heartbeat.LastHeartbeat);
    }

    [Fact]
    public async Task Above_Threshold_Outage_Extends_All_Active_Phase_Deadlines()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var outage = TimeSpan.FromMinutes(10);
        await StampHeartbeatAsync(nowUtc - outage);

        var b2 = (await AddBuyerAsync()).Id;
        var b3 = (await AddBuyerAsync()).Id;
        var t1 = NewActive(TransactionStatus.CREATED, acceptDeadline: nowUtc.AddMinutes(5));
        var t2 = NewActive(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, tradeOfferToSellerDeadline: nowUtc.AddMinutes(8), buyerId: b2);
        var t3 = NewActive(TransactionStatus.TRADE_OFFER_SENT_TO_BUYER, tradeOfferToBuyerDeadline: nowUtc.AddMinutes(12), buyerId: b3);
        Context.Set<Transaction>().AddRange(t1, t2, t3);
        await Context.SaveChangesAsync();

        var sut = BuildSut(recoveryThresholdSeconds: 60);
        var result = await sut.RunAsync(CancellationToken.None);

        Assert.True(result.ExtensionApplied);
        Assert.Equal(3, result.ExtendedTransactionCount);
        Assert.Equal(0, result.RescheduledPaymentJobCount);

        var p1 = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == t1.Id);
        var p2 = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == t2.Id);
        var p3 = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == t3.Id);

        Assert.Equal(nowUtc.AddMinutes(5) + outage, p1.AcceptDeadline);
        Assert.Equal(nowUtc.AddMinutes(8) + outage, p2.TradeOfferToSellerDeadline);
        Assert.Equal(nowUtc.AddMinutes(12) + outage, p3.TradeOfferToBuyerDeadline);
    }

    [Fact]
    public async Task Above_Threshold_Outage_Reschedules_ITEM_ESCROWED_Payment_Jobs()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var outage = TimeSpan.FromMinutes(20);
        await StampHeartbeatAsync(nowUtc - outage);

        var buyer = await AddBuyerAsync();
        var transaction = NewActive(
            TransactionStatus.ITEM_ESCROWED,
            paymentDeadline: nowUtc.AddMinutes(5),
            paymentTimeoutJobId: "old-payment",
            timeoutWarningJobId: "old-warning",
            buyerId: buyer.Id);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        var sut = BuildSut(recoveryThresholdSeconds: 60);
        var result = await sut.RunAsync(CancellationToken.None);

        Assert.True(result.ExtensionApplied);
        Assert.Equal(1, result.RescheduledPaymentJobCount);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(nowUtc.AddMinutes(5) + outage, persisted.PaymentDeadline);
        Assert.NotNull(persisted.PaymentTimeoutJobId);
        Assert.NotEqual("old-payment", persisted.PaymentTimeoutJobId);
        Assert.Contains("old-payment", _scheduler.DeletedJobIds);
        Assert.Contains("old-warning", _scheduler.DeletedJobIds);
    }

    [Fact]
    public async Task Frozen_And_Held_Transactions_Are_Skipped()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var outage = TimeSpan.FromMinutes(10);
        await StampHeartbeatAsync(nowUtc - outage);

        var frozen = NewActive(
            TransactionStatus.CREATED,
            acceptDeadline: nowUtc.AddMinutes(5));
        frozen.TimeoutFrozenAt = nowUtc.AddMinutes(-30);
        frozen.TimeoutFreezeReason = TimeoutFreezeReason.MAINTENANCE;
        frozen.TimeoutRemainingSeconds = 1800; // CK_Transactions_FreezeActive

        var heldBuyer = (await AddBuyerAsync()).Id;
        var held = NewActive(
            TransactionStatus.ITEM_ESCROWED,
            paymentDeadline: nowUtc.AddMinutes(5),
            buyerId: heldBuyer);
        held.IsOnHold = true;
        held.TimeoutFrozenAt = nowUtc.AddMinutes(-30);
        held.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        held.TimeoutRemainingSeconds = 1800; // CK_Transactions_FreezeActive
        held.EmergencyHoldAt = nowUtc.AddMinutes(-30);
        held.EmergencyHoldReason = "test";
        held.EmergencyHoldByAdminId = _seller.Id; // any User Id satisfies the FK

        Context.Set<Transaction>().AddRange(frozen, held);
        await Context.SaveChangesAsync();

        var sut = BuildSut(recoveryThresholdSeconds: 60);
        var result = await sut.RunAsync(CancellationToken.None);

        Assert.True(result.ExtensionApplied); // Heartbeat outage still detected.
        Assert.Equal(0, result.ExtendedTransactionCount);

        var pf = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == frozen.Id);
        var ph = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == held.Id);
        Assert.Equal(nowUtc.AddMinutes(5), pf.AcceptDeadline); // unchanged
        Assert.Equal(nowUtc.AddMinutes(5), ph.PaymentDeadline);
    }

    private async Task StampHeartbeatAsync(DateTime lastHeartbeat)
    {
        var heartbeat = await Context.Set<SystemHeartbeat>()
            .SingleAsync(h => h.Id == SeedConstants.SystemHeartbeatId);
        heartbeat.LastHeartbeat = lastHeartbeat;
        heartbeat.UpdatedAt = lastHeartbeat;
        await Context.SaveChangesAsync();
    }

    private async Task<User> AddBuyerAsync()
    {
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "7656" + Random.Shared.NextInt64(1_000_000_000_000L, 9_999_999_999_999L),
            SteamDisplayName = "Buyer-" + Guid.NewGuid().ToString("N")[..6],
        };
        Context.Set<User>().Add(buyer);
        await Context.SaveChangesAsync();
        return buyer;
    }

    private Transaction NewActive(
        TransactionStatus status,
        DateTime? acceptDeadline = null,
        DateTime? tradeOfferToSellerDeadline = null,
        DateTime? paymentDeadline = null,
        DateTime? tradeOfferToBuyerDeadline = null,
        string? paymentTimeoutJobId = null,
        string? timeoutWarningJobId = null,
        Guid? buyerId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerId = status == TransactionStatus.CREATED ? null : buyerId,
            BuyerRefundAddress = status == TransactionStatus.CREATED ? null : ValidWallet,
            BuyerIdentificationMethod = BuyerIdentificationMethod.OPEN_LINK,
            InviteToken = "tok-" + Guid.NewGuid().ToString("N")[..8],
            ItemAssetId = "100200300",
            ItemClassId = "abc-class",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = ValidWallet,
            PaymentTimeoutMinutes = 1440,
            AcceptDeadline = acceptDeadline,
            TradeOfferToSellerDeadline = tradeOfferToSellerDeadline,
            PaymentDeadline = paymentDeadline,
            TradeOfferToBuyerDeadline = tradeOfferToBuyerDeadline,
            PaymentTimeoutJobId = paymentTimeoutJobId,
            TimeoutWarningJobId = timeoutWarningJobId,
        };

    private RestartRecoveryService BuildSut(int recoveryThresholdSeconds)
    {
        var options = Options.Create(new TimeoutSchedulingOptions
        {
            DeadlineScannerIntervalSeconds = 30,
            DeadlineScannerBatchSize = 200,
            HeartbeatIntervalSeconds = 30,
            RecoveryThresholdSeconds = recoveryThresholdSeconds,
        });
        var scheduling = new TimeoutSchedulingService(Context, _scheduler, _clock);
        return new RestartRecoveryService(
            Context, scheduling, _clock, options,
            NullLogger<RestartRecoveryService>.Instance);
    }

    private sealed class CapturingScheduler : IBackgroundJobScheduler
    {
        public List<string> DeletedJobIds { get; } = new();

        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
            => Guid.NewGuid().ToString("N");
        public string Enqueue<T>(Expression<Action<T>> methodCall) => Guid.NewGuid().ToString("N");
        public bool Delete(string jobId)
        {
            DeletedJobIds.Add(jobId);
            return true;
        }
        public void AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>> methodCall, string cronExpression) { }
    }
}
