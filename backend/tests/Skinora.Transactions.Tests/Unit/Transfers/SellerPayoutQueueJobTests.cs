using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Transfers;

/// <summary>
/// Unit coverage for <see cref="SellerPayoutQueueJob"/> (WP1 — 02 §4.7,
/// 03 §2.4). Confirms the gas-fee-protection net is queued as a PENDING
/// SELLER_PAYOUT row, the gas estimate is snapshotted, and held / disputed /
/// non-delivered / already-paid / addressless transactions are skipped.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SellerPayoutQueueJobTests : IDisposable
{
    static SellerPayoutQueueJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly StubGasFeeSettingsProvider _settings;
    private readonly FakeTimeProvider _clock;
    private readonly SellerPayoutQueueJob _sut;

    public SellerPayoutQueueJobTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _settings = new StubGasFeeSettingsProvider
        {
            Settings = new GasFeeSettings(
                ProtectionRatio: 0.10m,
                MinRefundThresholdRatio: 2m,
                RefundGasFeeEstimateUsdt: 2m,
                PayoutGasFeeEstimateUsdt: 0.50m),
        };
        _clock = new FakeTimeProvider();
        _clock.SetUtcNow(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        _sut = new SellerPayoutQueueJob(
            _db,
            new RefundDecisionService(_settings),
            _settings,
            _clock,
            NullLogger<SellerPayoutQueueJob>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GasAboveThreshold_QueuesPendingPayout_WithNetAmountAndGasSnapshot()
    {
        // price 100, commission 2 → threshold 0.20; gasFee 0.50 > 0.20 →
        // overage 0.30 → net 99.70 (04 §7.3 worked example).
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m);

        await _sut.ExecuteAsync();

        var payout = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT);
        Assert.Equal(BlockchainTransactionStatus.PENDING, payout.Status);
        Assert.Equal(99.70m, payout.Amount);
        Assert.Equal(0.50m, payout.GasFee);
        Assert.Equal(tx.SellerPayoutAddress, payout.ToAddress);
        Assert.Equal(StablecoinType.USDT, payout.Token);
        Assert.Null(payout.PaymentAddressId);
        Assert.Null(payout.ActualTokenAddress);
        Assert.Equal(string.Empty, payout.FromAddress);
        Assert.Null(payout.NextAttemptAt);
    }

    [Fact]
    public async Task GasBelowThreshold_PaysFullPrice()
    {
        // commission 10 → threshold 1.0; gasFee 0.50 ≤ 1.0 → platform absorbs,
        // net = price.
        var tx = await SeedDeliveredAsync(price: 100m, commission: 10m);

        await _sut.ExecuteAsync();

        var payout = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT);
        Assert.Equal(100m, payout.Amount);
        Assert.Equal(0.50m, payout.GasFee);
    }

    [Fact]
    public async Task HeldTransaction_IsSkipped()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
        {
            t.IsOnHold = true;
            t.EmergencyHoldAt = _clock.GetUtcNow().UtcDateTime;
            t.EmergencyHoldReason = "test hold";
            t.EmergencyHoldByAdminId = t.SellerId; // existing user — satisfies FK.
            t.TimeoutFrozenAt = _clock.GetUtcNow().UtcDateTime;
            t.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
            t.TimeoutRemainingSeconds = 0;
        });

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    [Fact]
    public async Task DisputedTransaction_IsSkipped()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.HasActiveDispute = true);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    [Fact]
    public async Task NonDeliveredTransaction_IsSkipped()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.Status = TransactionStatus.PAYMENT_RECEIVED);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    [Fact]
    public async Task ExistingPayoutRow_IsNotDuplicated()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m);
        _db.Set<BlockchainTransaction>().Add(new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Type = BlockchainTransactionType.SELLER_PAYOUT,
            FromAddress = string.Empty,
            ToAddress = tx.SellerPayoutAddress,
            Amount = 99.70m,
            Token = StablecoinType.USDT,
            GasFee = 0.50m,
            Status = BlockchainTransactionStatus.PENDING,
            ConfirmationCount = 0,
            RetryCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var count = await _db.Set<BlockchainTransaction>().CountAsync(
            b => b.TransactionId == tx.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task EmptySellerPayoutAddress_IsSkipped()
    {
        var tx = await SeedDeliveredAsync(price: 100m, commission: 2m, configure: t =>
            t.SellerPayoutAddress = string.Empty);

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id));
    }

    private async Task<Transaction> SeedDeliveredAsync(
        decimal price, decimal commission, Action<Transaction>? configure = null)
    {
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000811",
            SteamDisplayName = "Seller",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000812",
            SteamDisplayName = "Buyer",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<User>().AddRange(seller, buyer);

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.ITEM_DELIVERED,
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
            Price = price,
            CommissionRate = 0.02m,
            CommissionAmount = commission,
            TotalAmount = price + commission,
            SellerPayoutAddress = "TSellerPayout00000000000000000000000",
            ItemDeliveredAt = _clock.GetUtcNow().UtcDateTime,
        };
        configure?.Invoke(tx);

        _db.Set<Transaction>().Add(tx);
        await _db.SaveChangesAsync();
        return tx;
    }

    private sealed class StubGasFeeSettingsProvider : IGasFeeSettingsProvider
    {
        public GasFeeSettings Settings { get; set; } =
            new(0.10m, 2m, 2m, 0.50m);

        public Task<GasFeeSettings> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Settings);
    }
}
