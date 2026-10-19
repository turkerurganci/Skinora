using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.PostCancel;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.PostCancel;

/// <summary>
/// T158 (h) doğrulaması — the cancel pipeline's hand-off
/// (<see cref="PostCancelMonitorStarter"/>) must carry the deposit's persisted
/// start block into <see cref="PostCancelMonitorStartRequestedEvent"/>; the
/// dispatcher forwards whatever the event holds
/// (<c>PostCancelMonitorStartDispatcherTests.T158h_*</c>), so a block dropped
/// here reaches the Ethereum service as "no start block" and the deposit is
/// read with T151's look-back rule instead of from its derivation block. The
/// integration twin (<c>Integration/PostCancel/PostCancelMonitorStarterTests</c>)
/// covers the idempotency branches; this one runs on SQLite so it needs no
/// Docker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PostCancelMonitorStarterStartBlockTests : IDisposable
{
    private const string DepositAddress = "0x5b38da6a701c568545dcfcb03fcb875f56beddc4";

    static PostCancelMonitorStarterStartBlockTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public PostCancelMonitorStarterStartBlockTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Theory]
    [InlineData(26_214_908L)]
    [InlineData(null)]
    public async Task T158h_The_Persisted_Start_Block_Rides_The_PostCancel_Event(long? monitorFromBlock)
    {
        // The null row is a pre-T158 / TRON deposit: the event carries null and
        // the HTTP client leaves the field out of the body.
        var cancelledAt = new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);
        var transactionId = await SeedActiveAddressAsync(monitorFromBlock, cancelledAt);
        var outbox = new RecordingOutboxService();
        var sut = new PostCancelMonitorStarter(
            _db,
            outbox,
            StablecoinContractRegistry.FromOptions(new StablecoinContractOptions()),
            NullLogger<PostCancelMonitorStarter>.Instance);

        await sut.RequestStartAsync(transactionId, cancelledAt, CancellationToken.None);

        var evt = Assert.IsType<PostCancelMonitorStartRequestedEvent>(Assert.Single(outbox.Published));
        Assert.Equal(DepositAddress, evt.Address);
        Assert.Equal(BlockchainNetwork.ETHEREUM, evt.Network);
        Assert.Equal(monitorFromBlock, evt.WatchFromBlock);
    }

    private async Task<Guid> SeedActiveAddressAsync(long? monitorFromBlock, DateTime nowUtc)
    {
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000001583",
            SteamDisplayName = "Seller",
            CreatedAt = nowUtc,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000001584",
            SteamDisplayName = "Buyer",
            CreatedAt = nowUtc,
        };
        _db.Set<User>().AddRange(seller, buyer);

        // The starter reads only the PaymentAddress; the transaction row is
        // there for the foreign key. Seeded as already cancelled because the
        // cancel handler commits the state change in the same unit of work.
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.CANCELLED_ADMIN,
            SellerId = seller.Id,
            BuyerId = buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = buyer.SteamId,
            BuyerRefundAddress = "0x8ba1f109551bd432803012645ac136ddd64dba72",
            ItemAssetId = "asset-1",
            ItemClassId = "cls",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = "0x71c7656ec7ab88b098defb751b7401b5f6d8976f",
            Network = BlockchainNetwork.ETHEREUM,
            CancelledBy = CancelledByType.ADMIN,
            CancelReason = "admin cancel (test)",
            CancelledAt = nowUtc,
        };
        _db.Set<Transaction>().Add(transaction);

        _db.Set<PaymentAddress>().Add(new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            Network = BlockchainNetwork.ETHEREUM,
            Address = DepositAddress,
            HdWalletIndex = 42,
            ExpectedAmount = transaction.TotalAmount,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
            MonitorFromBlock = monitorFromBlock,
            CreatedAt = nowUtc,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return transaction.Id;
    }

    private sealed class RecordingOutboxService : IOutboxService
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
