using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;
using Skinora.Realtime.Application.Countdown;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Realtime.Tests.Unit;

/// <summary>
/// Unit coverage for <see cref="CountdownSyncBroadcaster.BroadcastOnceAsync"/>:
/// active-status filter, phase resolution, frozen vs running countdown
/// remaining-seconds, and per-transaction failure isolation.
/// </summary>
public class CountdownSyncBroadcasterTests : IDisposable
{
    private readonly SqliteConnection _connection;

    static CountdownSyncBroadcasterTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    public CountdownSyncBroadcasterTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private async Task<(IServiceProvider Services, RecordingRealtimePublisher Publisher)>
        BuildHostAsync(IEnumerable<Transaction> seed)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        var publisher = new RecordingRealtimePublisher();
        services.AddSingleton<ITransactionRealtimePublisher>(publisher);

        var sp = services.BuildServiceProvider();
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();

            var seedList = seed.ToList();
            var userIds = seedList
                .SelectMany(t => new[]
                {
                    t.SellerId,
                    t.BuyerId ?? Guid.Empty,
                    t.EmergencyHoldByAdminId ?? Guid.Empty,
                })
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            var i = 1;
            foreach (var id in userIds)
            {
                db.Set<User>().Add(new User
                {
                    Id = id,
                    SteamId = $"7656119800000{i:D4}",
                    SteamDisplayName = $"User-{i}",
                });
                i++;
            }
            await db.SaveChangesAsync();

            db.Set<Transaction>().AddRange(seedList);
            await db.SaveChangesAsync();
        }
        return (sp, publisher);
    }

    private static Transaction NewTx(
        TransactionStatus status,
        DateTime? acceptDeadline = null,
        DateTime? sellerConfirmDeadline = null,
        DateTime? paymentDeadline = null,
        DateTime? deliveryDeadline = null,
        bool isOnHold = false,
        TimeoutFreezeReason? freezeReason = null,
        int? remainingSeconds = null)
    {
        var isCancelled = status is TransactionStatus.CANCELLED_TIMEOUT
            or TransactionStatus.CANCELLED_SELLER
            or TransactionStatus.CANCELLED_BUYER
            or TransactionStatus.CANCELLED_ADMIN;

        return new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            ItemAssetId = "ASSET",
            ItemClassId = "CLS",
            ItemName = "Item",
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000001",
            StablecoinType = StablecoinType.USDT,
            Price = 10,
            CommissionRate = 0,
            CommissionAmount = 0,
            TotalAmount = 10,
            SellerPayoutAddress = "TRC20XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX",
            PaymentTimeoutMinutes = 60,
            AcceptDeadline = acceptDeadline,
            SellerConfirmDeadline = sellerConfirmDeadline,
            PaymentDeadline = paymentDeadline,
            DeliveryDeadline = deliveryDeadline,
            IsOnHold = isOnHold,
            // CK_Transactions_FreezeHold_Reverse — IsOnHold = 1 requires
            // TimeoutFrozenAt set + freezeReason = EMERGENCY_HOLD.
            TimeoutFrozenAt = isOnHold ? DateTime.UtcNow : null,
            TimeoutFreezeReason = freezeReason,
            TimeoutRemainingSeconds = remainingSeconds,
            // CK_Transactions_Hold — IsOnHold = 1 requires the EmergencyHold tuple.
            EmergencyHoldAt = isOnHold ? DateTime.UtcNow : null,
            EmergencyHoldReason = isOnHold ? "test hold" : null,
            EmergencyHoldByAdminId = isOnHold ? Guid.NewGuid() : null,
            // CK_Transactions_Cancel — terminal cancel statuses require the
            // cancellation tuple to be non-null.
            CancelledBy = isCancelled ? CancelledByType.TIMEOUT : null,
            CancelReason = isCancelled ? "test seed cancellation" : null,
            CancelledAt = isCancelled ? DateTime.UtcNow : null,
        };
    }

    private static CountdownSyncBroadcaster Build(
        IServiceProvider sp,
        TimeProvider clock) =>
        new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CountdownSyncOptions { Enabled = true, Interval = TimeSpan.FromSeconds(30) }),
            clock,
            NullLogger<CountdownSyncBroadcaster>.Instance);

    [Fact]
    public async Task BroadcastOnce_OnlyActiveTransactions_AreScanned()
    {
        var nowUtc = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(nowUtc);

        var active = NewTx(
            TransactionStatus.CREATED,
            acceptDeadline: nowUtc.AddMinutes(10));
        var completed = NewTx(TransactionStatus.COMPLETED);
        var cancelled = NewTx(TransactionStatus.CANCELLED_TIMEOUT);
        var flagged = NewTx(TransactionStatus.FLAGGED);

        var (sp, publisher) = await BuildHostAsync([active, completed, cancelled, flagged]);
        await Build(sp, clock).BroadcastOnceAsync(CancellationToken.None);

        var sync = Assert.IsType<TransactionRealtimePayloads.CountdownSync>(
            publisher.Calls.Single().Payload);
        Assert.Equal(active.Id, sync.TransactionId);
        Assert.Equal(TimeoutPhase.Accept, sync.TimeoutType);
    }

    [Fact]
    public async Task BroadcastOnce_RunningCountdown_ComputesRemainingSecondsFromDeadline()
    {
        var nowUtc = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(nowUtc);

        var tx = NewTx(
            TransactionStatus.SELLER_CONFIRMED,
            paymentDeadline: nowUtc.AddSeconds(127));

        var (sp, publisher) = await BuildHostAsync([tx]);
        await Build(sp, clock).BroadcastOnceAsync(CancellationToken.None);

        var sync = Assert.IsType<TransactionRealtimePayloads.CountdownSync>(
            publisher.Calls.Single().Payload);
        Assert.Equal(TimeoutPhase.Payment, sync.TimeoutType);
        Assert.Equal(127, sync.RemainingSeconds);
        Assert.False(sync.Frozen);
        Assert.Null(sync.FrozenReason);
    }

    [Fact]
    public async Task BroadcastOnce_FrozenByEmergencyHold_UsesSnapshotRemainingAndExposesReason()
    {
        var nowUtc = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(nowUtc);

        var tx = NewTx(
            TransactionStatus.SELLER_CONFIRMED,
            paymentDeadline: nowUtc.AddSeconds(60),
            isOnHold: true,
            freezeReason: TimeoutFreezeReason.EMERGENCY_HOLD,
            remainingSeconds: 720);

        var (sp, publisher) = await BuildHostAsync([tx]);
        await Build(sp, clock).BroadcastOnceAsync(CancellationToken.None);

        var sync = Assert.IsType<TransactionRealtimePayloads.CountdownSync>(
            publisher.Calls.Single().Payload);
        Assert.True(sync.Frozen);
        Assert.Equal(TimeoutFreezeReason.EMERGENCY_HOLD, sync.FrozenReason);
        Assert.Equal(720, sync.RemainingSeconds);
    }

    [Fact]
    public async Task BroadcastOnce_DeadlineInPast_ClampsToZero()
    {
        var nowUtc = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(nowUtc);

        var tx = NewTx(
            TransactionStatus.CREATED,
            acceptDeadline: nowUtc.AddMinutes(-5));

        var (sp, publisher) = await BuildHostAsync([tx]);
        await Build(sp, clock).BroadcastOnceAsync(CancellationToken.None);

        var sync = Assert.IsType<TransactionRealtimePayloads.CountdownSync>(
            publisher.Calls.Single().Payload);
        Assert.Equal(0, sync.RemainingSeconds);
    }

    [Theory]
    [InlineData(TransactionStatus.CREATED, TimeoutPhase.Accept)]
    [InlineData(TransactionStatus.ACCEPTED, TimeoutPhase.SellerConfirm)]
    [InlineData(TransactionStatus.SELLER_CONFIRMED, TimeoutPhase.Payment)]
    [InlineData(TransactionStatus.PAYMENT_RECEIVED, TimeoutPhase.Delivery)]
    public async Task BroadcastOnce_PhaseMatchesStatus(
        TransactionStatus status, TimeoutPhase expectedPhase)
    {
        var nowUtc = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(nowUtc);
        var deadline = nowUtc.AddMinutes(5);

        var tx = NewTx(
            status,
            acceptDeadline: deadline,
            sellerConfirmDeadline: deadline,
            paymentDeadline: deadline,
            deliveryDeadline: deadline);

        var (sp, publisher) = await BuildHostAsync([tx]);
        await Build(sp, clock).BroadcastOnceAsync(CancellationToken.None);

        var sync = Assert.IsType<TransactionRealtimePayloads.CountdownSync>(
            publisher.Calls.Single().Payload);
        Assert.Equal(expectedPhase, sync.TimeoutType);
    }

    private sealed class FakeClock : TimeProvider
    {
        private readonly DateTimeOffset _utc;
        public FakeClock(DateTime utc) => _utc = new DateTimeOffset(utc, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _utc;
    }
}
