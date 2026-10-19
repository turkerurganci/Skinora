using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Transactions.Tests.Helpers;
using Skinora.Transactions.Tests.Integration.Lifecycle;
using Skinora.Users.Application.Settings;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Lifecycle;

/// <summary>
/// T158 (h) doğrulaması — confirm-ready (<see cref="TransactionReadinessService"/>
/// Stage 10b) is the FIRST arming of a deposit's payment monitor, so it is the
/// path every Ethereum payment rides on. The persisted start block must reach
/// <see cref="PaymentMonitorStartRequestedEvent"/>; the dispatcher forwards
/// whatever the event holds (<c>PaymentMonitorStartDispatcherTests</c>). The
/// pipeline itself is covered by the integration suite
/// (<c>Integration/Lifecycle/TransactionReadinessServiceTests</c>); this one runs
/// the same happy path on SQLite so it needs no Docker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TransactionReadinessStartBlockTests : IDisposable
{
    private const string SellerSteamId = "76561198000001585";
    private const string BuyerSteamId = "76561198000001586";
    private const string ItemAssetId = "27348562891";
    private const string ItemClassId = "310776959";
    private const string ItemInstanceId = "188530139";
    private const string DepositAddress = "0x5b38da6a701c568545dcfcb03fcb875f56beddc4";
    private const ulong SteamId64ToId32Offset = 76561197960265728UL;

    private static readonly string BuyerTradeUrl =
        $"https://steamcommunity.com/tradeoffer/new/?partner={ulong.Parse(BuyerSteamId) - SteamId64ToId32Offset}&token=AbCdEfGh";

    static TransactionReadinessStartBlockTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly FakeTimeProvider _clock =
        new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    public TransactionReadinessStartBlockTests()
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
    public async Task T158h_The_Persisted_Start_Block_Rides_The_Payment_Monitor_Event(long? monitorFromBlock)
    {
        // The null row is a pre-T158 / TRON deposit: the event carries null and
        // the HTTP client leaves the field out of the body.
        var (sellerId, transactionId) = await SeedAcceptedWithDepositAsync(monitorFromBlock);
        var outbox = new RecordingOutboxService();

        var outcome = await BuildSut(outbox).ConfirmReadyAsync(sellerId, transactionId, CancellationToken.None);

        Assert.Equal(ConfirmReadyStatus.Confirmed, outcome.Status);
        var evt = Assert.IsType<PaymentMonitorStartRequestedEvent>(
            Assert.Single(outbox.Published, e => e is PaymentMonitorStartRequestedEvent));
        Assert.Equal(DepositAddress, evt.Address);
        Assert.Equal(BlockchainNetwork.ETHEREUM, evt.Network);
        Assert.Equal(monitorFromBlock, evt.WatchFromBlock);
    }

    private TransactionReadinessService BuildSut(IOutboxService outbox)
    {
        var inventory = new FakeSteamInventoryReader();
        inventory.Register(SellerSteamId, new InventoryItemSnapshot(
            AssetId: ItemAssetId,
            ClassId: ItemClassId,
            InstanceId: ItemInstanceId,
            Name: "AK-47 | Redline",
            MarketHashName: "AK-47 | Redline (Field-Tested)",
            IconUrl: null,
            Exterior: "Field-Tested",
            Type: "Rifle",
            InspectLink: null,
            IsTradeable: true));

        return new TransactionReadinessService(
            _db,
            inventory,
            new TradeUrlParser(),
            new FixedTradeHoldChecker(new TradeHoldResult(Available: true, Active: true, SetupGuideUrl: null)),
            new FakeSteamTradeEligibilityChecker(),
            new TimeoutSchedulingService(_db, new RecordingJobScheduler(), _clock),
            outbox,
            StablecoinContractRegistry.FromOptions(new StablecoinContractOptions()),
            NullLogger<TransactionReadinessService>.Instance,
            _clock);
    }

    private async Task<(Guid SellerId, Guid TransactionId)> SeedAcceptedWithDepositAsync(long? monitorFromBlock)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteamId,
            SteamDisplayName = "Seller",
            MobileAuthenticatorVerified = true,
            CreatedAt = nowUtc,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = BuyerSteamId,
            SteamDisplayName = "Buyer",
            MobileAuthenticatorVerified = true,
            CreatedAt = nowUtc,
        };
        _db.Set<User>().AddRange(seller, buyer);

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.ACCEPTED,
            SellerId = seller.Id,
            BuyerId = buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = BuyerSteamId,
            BuyerRefundAddress = "0x8ba1f109551bd432803012645ac136ddd64dba72",
            BuyerTradeUrl = BuyerTradeUrl,
            ItemAssetId = ItemAssetId,
            ItemClassId = ItemClassId,
            ItemInstanceId = ItemInstanceId,
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = "0x71c7656ec7ab88b098defb751b7401b5f6d8976f",
            PaymentTimeoutMinutes = 1440,
            AcceptedAt = nowUtc.AddMinutes(-5),
            Network = BlockchainNetwork.ETHEREUM,
            SellerConfirmDeadline = nowUtc.AddHours(1),
        };
        _db.Set<Transaction>().Add(transaction);

        _db.Set<PaymentAddress>().Add(new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            Network = BlockchainNetwork.ETHEREUM,
            Address = DepositAddress,
            HdWalletIndex = 43,
            ExpectedAmount = transaction.TotalAmount,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
            MonitorFromBlock = monitorFromBlock,
            CreatedAt = nowUtc,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (seller.Id, transaction.Id);
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

    private sealed class FixedTradeHoldChecker(TradeHoldResult result) : ITradeHoldChecker
    {
        public Task<TradeHoldResult> CheckAsync(
            string steamId64, string tradeOfferAccessToken, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class RecordingJobScheduler : IBackgroundJobScheduler
    {
        private int _next;

        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay) => $"job-{++_next}";

        public string Enqueue<T>(Expression<Action<T>> methodCall) => $"job-{++_next}";

        public bool Delete(string jobId) => true;

        public void AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>> methodCall, string cronExpression)
        {
        }
    }
}
