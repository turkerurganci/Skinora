using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Transfers;

/// <summary>
/// Unit coverage for <see cref="PayoutCompletedConsumer"/> (WP1 — 03 §2.4
/// step 6). Confirms ITEM_DELIVERED → COMPLETED on payout confirmation, the
/// CompletedAt stamp, idempotency on redelivery, the hold guard, and no-ops
/// for missing / wrong-state transactions.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PayoutCompletedConsumerTests : IDisposable
{
    static PayoutCompletedConsumerTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly FakeTimeProvider _clock;
    private readonly PayoutCompletedConsumer _sut;

    public PayoutCompletedConsumerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _clock = new FakeTimeProvider();
        _clock.SetUtcNow(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        _sut = new PayoutCompletedConsumer(
            _db, NullLogger<PayoutCompletedConsumer>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task DeliveredTransaction_FiresComplete_AndStampsCompletedAt()
    {
        var tx = await SeedAsync(TransactionStatus.ITEM_DELIVERED);

        await _sut.Handle(EventFor(tx.Id), CancellationToken.None);

        var reloaded = await _db.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.COMPLETED, reloaded.Status);
        // COMPLETED.OnEntry stamps CompletedAt via DateTime.UtcNow (same as the
        // other lifecycle milestones), so assert it is set rather than equal to
        // the fake clock.
        Assert.NotNull(reloaded.CompletedAt);
    }

    [Fact]
    public async Task AlreadyCompleted_IsNoOp()
    {
        var tx = await SeedAsync(TransactionStatus.COMPLETED, t =>
            t.CompletedAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(-5));

        await _sut.Handle(EventFor(tx.Id), CancellationToken.None);

        var reloaded = await _db.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.COMPLETED, reloaded.Status);
        // Untouched — CompletedAt is the original stamp, not re-stamped.
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddMinutes(-5), reloaded.CompletedAt);
    }

    [Fact]
    public async Task HeldTransaction_IsNotCompleted()
    {
        var tx = await SeedAsync(TransactionStatus.ITEM_DELIVERED, t =>
        {
            t.IsOnHold = true;
            t.EmergencyHoldAt = _clock.GetUtcNow().UtcDateTime;
            t.EmergencyHoldReason = "test hold";
            t.EmergencyHoldByAdminId = t.SellerId; // existing user — satisfies FK.
            t.TimeoutFrozenAt = _clock.GetUtcNow().UtcDateTime;
            t.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
            t.TimeoutRemainingSeconds = 0;
        });

        await _sut.Handle(EventFor(tx.Id), CancellationToken.None);

        var reloaded = await _db.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, reloaded.Status);
        Assert.Null(reloaded.CompletedAt);
    }

    [Fact]
    public async Task WrongState_IsNoOp()
    {
        var tx = await SeedAsync(TransactionStatus.PAYMENT_RECEIVED);

        await _sut.Handle(EventFor(tx.Id), CancellationToken.None);

        var reloaded = await _db.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, reloaded.Status);
    }

    [Fact]
    public async Task MissingTransaction_IsNoOp()
    {
        // No throw — a payout event for an unknown transaction is logged and dropped.
        await _sut.Handle(EventFor(Guid.NewGuid()), CancellationToken.None);
    }

    private static PayoutCompletedEvent EventFor(Guid transactionId) =>
        new(
            EventId: Guid.NewGuid(),
            TransactionId: transactionId,
            PayoutTxHash: "0xPayout" + transactionId.ToString("N"),
            NetAmount: 99.70m,
            OccurredAt: new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc));

    private async Task<Transaction> SeedAsync(
        TransactionStatus status, Action<Transaction>? configure = null)
    {
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000821",
            SteamDisplayName = "Seller",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000822",
            SteamDisplayName = "Buyer",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<User>().AddRange(seller, buyer);

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = seller.Id,
            BuyerId = buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000913",
            BuyerRefundAddress = "TBuyerRefund000000000000000000000000",
            ItemAssetId = "asset-1",
            ItemClassId = "cls",
            ItemName = "AK-47 | Redline",
            DeliveredBuyerAssetId = "delivered-asset-1",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = "TSellerPayout00000000000000000000000",
            ItemDeliveredAt = _clock.GetUtcNow().UtcDateTime,
        };
        configure?.Invoke(tx);

        _db.Set<Transaction>().Add(tx);
        await _db.SaveChangesAsync();
        return tx;
    }
}
