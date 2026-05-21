using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Lifecycle;

/// <summary>
/// Integration coverage for <see cref="TransactionListService"/> (T83a —
/// 07 §7.1). Validates the service against real SQL Server semantics so
/// that the FK to <c>Users</c>, the EMERGENCY_HOLD invariant trio and the
/// filtered-index on (<c>Status</c>, <c>IsDeleted</c>) all behave as in
/// production. The pure-logic branches (tab mapping, projection, clamps)
/// are exercised by <c>Unit/Lifecycle/TransactionListServiceTests</c>.
/// </summary>
public class TransactionListServiceTests : IntegrationTestBase
{
    static TransactionListServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string ValidWallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";

    private User _seller = null!;
    private User _buyer = null!;
    private User _stranger = null!;
    private FakeTimeProvider _clock = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000060",
            SteamDisplayName = "SellerPlayer",
            SteamAvatarUrl = "https://steamcdn.example/seller.jpg",
            DefaultPayoutAddress = ValidWallet,
            CreatedAt = DateTime.UtcNow.AddDays(-200),
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000061",
            SteamDisplayName = "BuyerPlayer",
            SteamAvatarUrl = "https://steamcdn.example/buyer.jpg",
            CreatedAt = DateTime.UtcNow.AddDays(-200),
        };
        _stranger = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000062",
            SteamDisplayName = "StrangerPlayer",
            CreatedAt = DateTime.UtcNow.AddDays(-200),
        };
        context.Set<User>().AddRange(_seller, _buyer, _stranger);
        await context.SaveChangesAsync();

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 21, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Party_Filter_Returns_Only_Caller_Sided_Rows()
    {
        await CreateTransactionAsync(TransactionStatus.CREATED, sellerId: _seller.Id);
        await CreateTransactionAsync(TransactionStatus.ACCEPTED, sellerId: _seller.Id, buyerId: _buyer.Id);
        await CreateTransactionAsync(TransactionStatus.CREATED, sellerId: _stranger.Id);

        var sut = BuildSut();
        var sellerResult = await sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);
        var buyerResult = await sut.ListAsync(_buyer.Id, Query(TransactionListTab.Active), default);
        var strangerResult = await sut.ListAsync(_stranger.Id, Query(TransactionListTab.Active), default);

        Assert.Equal(2, sellerResult.TotalCount);   // seller's CREATED + ACCEPTED
        Assert.Equal(1, buyerResult.TotalCount);    // buyer's ACCEPTED only
        Assert.Equal(1, strangerResult.TotalCount); // stranger's CREATED only
    }

    [Fact]
    public async Task Three_Tabs_Filter_Status_Sets_Independently()
    {
        await CreateTransactionAsync(TransactionStatus.CREATED, sellerId: _seller.Id);
        await CreateTransactionAsync(TransactionStatus.COMPLETED, sellerId: _seller.Id);
        await CreateTransactionAsync(TransactionStatus.CANCELLED_BUYER, sellerId: _seller.Id, buyerId: _buyer.Id);

        var sut = BuildSut();
        var active = await sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);
        var completed = await sut.ListAsync(_seller.Id, Query(TransactionListTab.Completed), default);
        var cancelled = await sut.ListAsync(_seller.Id, Query(TransactionListTab.Cancelled), default);

        Assert.Equal(1, active.TotalCount);
        Assert.Equal("CREATED", Assert.Single(active.Items).Status);
        Assert.Equal(1, completed.TotalCount);
        Assert.Equal("COMPLETED", Assert.Single(completed.Items).Status);
        Assert.Equal(1, cancelled.TotalCount);
        Assert.Equal("CANCELLED_BUYER", Assert.Single(cancelled.Items).Status);
    }

    [Fact]
    public async Task EMERGENCY_HOLD_Projection_Overrides_Real_Status()
    {
        var tx = await CreateTransactionAsync(
            TransactionStatus.ITEM_ESCROWED, sellerId: _seller.Id, buyerId: _buyer.Id);
        // 06 §3.5 invariant trio: IsOnHold=1 ↔ EmergencyHold{At,Reason,ByAdmin}
        // NOT NULL ↔ TimeoutFrozenAt + Reason='EMERGENCY_HOLD' + RemainingSeconds NOT NULL.
        tx.IsOnHold = true;
        tx.EmergencyHoldAt = _clock.GetUtcNow().UtcDateTime;
        tx.EmergencyHoldReason = "Sanctions match";
        tx.EmergencyHoldByAdminId = _seller.Id;
        tx.PreviousStatusBeforeHold = (int)TransactionStatus.ITEM_ESCROWED;
        tx.TimeoutFrozenAt = tx.EmergencyHoldAt;
        tx.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        tx.TimeoutRemainingSeconds = 0;
        Context.Set<Transaction>().Update(tx);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var result = await sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        var item = Assert.Single(result.Items);
        Assert.Equal("EMERGENCY_HOLD", item.Status);
    }

    [Fact]
    public async Task Pagination_Splits_Result_Set_Newest_First()
    {
        // Five active rows; CreatedAt is set on Add by the audit pipeline, then
        // overridden here to control deterministic ordering.
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var tx = await CreateTransactionAsync(TransactionStatus.CREATED, sellerId: _seller.Id);
            tx.CreatedAt = nowUtc.AddMinutes(-i); // i=0 is newest
            ids.Add(tx.Id);
        }
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var page1 = await sut.ListAsync(_seller.Id, new TransactionListQuery(
            TransactionListTab.Active, Page: 1, PageSize: 2), default);
        var page2 = await sut.ListAsync(_seller.Id, new TransactionListQuery(
            TransactionListTab.Active, Page: 2, PageSize: 2), default);
        var page3 = await sut.ListAsync(_seller.Id, new TransactionListQuery(
            TransactionListTab.Active, Page: 3, PageSize: 2), default);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(new[] { ids[0], ids[1] }, page1.Items.Select(i => i.Id));
        Assert.Equal(new[] { ids[2], ids[3] }, page2.Items.Select(i => i.Id));
        Assert.Equal(new[] { ids[4] }, page3.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Counterparty_Snapshot_Matches_User_Row()
    {
        await CreateTransactionAsync(TransactionStatus.ACCEPTED, sellerId: _seller.Id, buyerId: _buyer.Id);

        var sut = BuildSut();
        var fromSeller = await sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);
        var fromBuyer = await sut.ListAsync(_buyer.Id, Query(TransactionListTab.Active), default);

        var sellerView = Assert.Single(fromSeller.Items);
        var buyerView = Assert.Single(fromBuyer.Items);
        Assert.Equal(_buyer.SteamId, sellerView.Counterparty!.SteamId);
        Assert.Equal(_seller.SteamId, buyerView.Counterparty!.SteamId);
    }

    private static TransactionListQuery Query(TransactionListTab tab)
        => new(tab, Page: 1, PageSize: 20);

    private async Task<Transaction> CreateTransactionAsync(
        TransactionStatus status,
        Guid sellerId,
        Guid? buyerId = null)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = sellerId,
            BuyerId = buyerId,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198999999999",
            ItemAssetId = "27348562891",
            ItemClassId = "abc-class",
            ItemName = "AK-47 | Redline",
            ItemIconUrl = "https://steamcdn.example/ak.png",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = ValidWallet,
            PaymentTimeoutMinutes = 1440,
            AcceptDeadline = status == TransactionStatus.CREATED ? nowUtc.AddHours(1) : null,
            AcceptedAt = status == TransactionStatus.ACCEPTED ? nowUtc.AddMinutes(-5) : null,
            CompletedAt = status == TransactionStatus.COMPLETED ? nowUtc.AddMinutes(-1) : null,
            CancelledAt = IsCancelled(status) ? nowUtc.AddMinutes(-1) : null,
            CancelledBy = IsCancelled(status) ? CancelledByType.BUYER : null,
            CancelReason = IsCancelled(status) ? "Test iptal sebebi (>=10 char)" : null,
        };
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx;
    }

    private static bool IsCancelled(TransactionStatus status) =>
        status is TransactionStatus.CANCELLED_TIMEOUT
            or TransactionStatus.CANCELLED_SELLER
            or TransactionStatus.CANCELLED_BUYER
            or TransactionStatus.CANCELLED_ADMIN;

    private TransactionListService BuildSut() => new(Context, _clock);
}
