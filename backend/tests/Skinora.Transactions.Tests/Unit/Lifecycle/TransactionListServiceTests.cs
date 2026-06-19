using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Lifecycle;

/// <summary>
/// Unit-level coverage for <see cref="TransactionListService"/> (T83a —
/// 07 §7.1). Exercises the pure-logic surface: tab→status mapping,
/// EMERGENCY_HOLD projection, activeTimeout resolver (06 §3.5 matrix),
/// userRole resolver, pagination clamping, and counterparty resolution.
/// Backed by a SQLite in-memory DbContext so the service runs end-to-end
/// without spinning up SQL Server (deeper FK/CHECK behavior is covered
/// by <c>Integration/Lifecycle/TransactionListServiceTests</c>).
/// </summary>
[Trait("Category", "Unit")]
public sealed class TransactionListServiceTests : IDisposable
{
    static TransactionListServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private const string ValidWallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly FakeTimeProvider _clock;
    private readonly TransactionListService _sut;

    private User _seller = null!;
    private User _buyer = null!;
    private User _other = null!;

    public TransactionListServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 21, 12, 0, 0, TimeSpan.Zero));
        _sut = new TransactionListService(_db, _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ─── Tab → status filter mapping (07 §7.1 tab table) ───────────────────

    [Fact]
    public async Task Active_Tab_Returns_Only_Active_Statuses()
    {
        await SeedUsersAsync();
        await SeedTxAsync(TransactionStatus.CREATED, sellerId: _seller.Id);
        await SeedTxAsync(TransactionStatus.ITEM_DELIVERED, sellerId: _seller.Id);
        await SeedTxAsync(TransactionStatus.FLAGGED, sellerId: _seller.Id);
        await SeedTxAsync(TransactionStatus.COMPLETED, sellerId: _seller.Id);
        await SeedTxAsync(TransactionStatus.CANCELLED_TIMEOUT, sellerId: _seller.Id);

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Items, item =>
            Assert.Contains(item.Status, new[] { "CREATED", "ITEM_DELIVERED", "FLAGGED" }));
    }

    [Fact]
    public async Task Completed_Tab_Returns_Only_Completed()
    {
        await SeedUsersAsync();
        await SeedTxAsync(TransactionStatus.COMPLETED, sellerId: _seller.Id);
        await SeedTxAsync(TransactionStatus.ITEM_DELIVERED, sellerId: _seller.Id);
        await SeedTxAsync(TransactionStatus.CANCELLED_SELLER, sellerId: _seller.Id);

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Completed), default);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("COMPLETED", Assert.Single(result.Items).Status);
    }

    [Fact]
    public async Task Cancelled_Tab_Returns_All_Cancelled_Variants()
    {
        await SeedUsersAsync();
        await SeedTxAsync(TransactionStatus.CANCELLED_TIMEOUT, sellerId: _seller.Id);
        await SeedTxAsync(TransactionStatus.CANCELLED_SELLER, sellerId: _seller.Id);
        await SeedTxAsync(TransactionStatus.CANCELLED_BUYER, sellerId: _seller.Id);
        await SeedTxAsync(TransactionStatus.CANCELLED_ADMIN, sellerId: _seller.Id);
        await SeedTxAsync(TransactionStatus.ACCEPTED, sellerId: _seller.Id);

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Cancelled), default);

        Assert.Equal(4, result.TotalCount);
        Assert.All(result.Items, item =>
            Assert.StartsWith("CANCELLED_", item.Status));
    }

    // ─── Party filter ───────────────────────────────────────────────────────

    [Fact]
    public async Task Excludes_Transactions_Where_Caller_Is_Not_A_Party()
    {
        await SeedUsersAsync();
        await SeedTxAsync(TransactionStatus.CREATED, sellerId: _other.Id);

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Includes_Transactions_Where_Caller_Is_Buyer()
    {
        await SeedUsersAsync();
        await SeedTxAsync(TransactionStatus.ACCEPTED, sellerId: _seller.Id, buyerId: _buyer.Id);

        var result = await _sut.ListAsync(_buyer.Id, Query(TransactionListTab.Active), default);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("buyer", Assert.Single(result.Items).UserRole);
    }

    [Fact]
    public async Task Excludes_Soft_Deleted_Transactions()
    {
        await SeedUsersAsync();
        var tx = await SeedTxAsync(TransactionStatus.CREATED, sellerId: _seller.Id);
        tx.IsDeleted = true;
        tx.DeletedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync();

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        Assert.Equal(0, result.TotalCount);
    }

    // ─── EMERGENCY_HOLD projection (07 §7.1 note, 06 §3.5) ─────────────────

    [Fact]
    public async Task IsOnHold_Projects_Status_As_EMERGENCY_HOLD()
    {
        await SeedUsersAsync();
        var tx = await SeedTxAsync(TransactionStatus.ACCEPTED, sellerId: _seller.Id, buyerId: _buyer.Id);
        ApplyEmergencyHold(tx, _seller.Id);
        await _db.SaveChangesAsync();

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        Assert.Equal("EMERGENCY_HOLD", Assert.Single(result.Items).Status);
    }

    [Fact]
    public async Task IsOnHold_False_Preserves_Real_Status_Name()
    {
        await SeedUsersAsync();
        await SeedTxAsync(TransactionStatus.ITEM_ESCROWED, sellerId: _seller.Id, buyerId: _buyer.Id);

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        Assert.Equal("ITEM_ESCROWED", Assert.Single(result.Items).Status);
    }

    // ─── activeTimeout resolver (06 §3.5 matrix) ───────────────────────────

    [Theory]
    [InlineData(TransactionStatus.CREATED, "accept")]
    [InlineData(TransactionStatus.ACCEPTED, "trade_offer_seller")]
    [InlineData(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, "trade_offer_seller")]
    [InlineData(TransactionStatus.ITEM_ESCROWED, "payment")]
    [InlineData(TransactionStatus.PAYMENT_RECEIVED, "trade_offer_buyer")]
    [InlineData(TransactionStatus.TRADE_OFFER_SENT_TO_BUYER, "trade_offer_buyer")]
    public async Task ActiveTimeout_Maps_Phase_Per_Status(TransactionStatus status, string expectedType)
    {
        await SeedUsersAsync();
        var tx = await SeedTxAsync(status, sellerId: _seller.Id, buyerId: _buyer.Id);
        // Seed all deadline fields — the matrix resolver picks the right one.
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        tx.AcceptDeadline = nowUtc.AddMinutes(10);
        tx.TradeOfferToSellerDeadline = nowUtc.AddMinutes(20);
        tx.PaymentDeadline = nowUtc.AddMinutes(30);
        tx.TradeOfferToBuyerDeadline = nowUtc.AddMinutes(40);
        await _db.SaveChangesAsync();

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        var item = Assert.Single(result.Items);
        Assert.NotNull(item.ActiveTimeout);
        Assert.Equal(expectedType, item.ActiveTimeout.Type);
        Assert.Equal(75, item.ActiveTimeout.WarningThresholdPercent);
    }

    [Fact]
    public async Task ActiveTimeout_WarningThreshold_Reflects_Configured_Ratio()
    {
        // WP12 (T83a) — the read-path now derives WarningThresholdPercent from
        // the timeout_warning_ratio SystemSetting (ratio × 100), not a hardcoded
        // 75. Lower the seeded 0.75 to 0.50 and the surfaced percent follows.
        await SeedUsersAsync();
        var tx = await SeedTxAsync(TransactionStatus.CREATED, sellerId: _seller.Id, buyerId: _buyer.Id);
        tx.AcceptDeadline = _clock.GetUtcNow().UtcDateTime.AddMinutes(10);
        await _db.SaveChangesAsync();

        var ratioRow = await _db.Set<SystemSetting>()
            .SingleAsync(s => s.Key == "timeout_warning_ratio");
        ratioRow.Value = "0.50";
        ratioRow.IsConfigured = true;
        await _db.SaveChangesAsync();

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        var item = Assert.Single(result.Items);
        Assert.NotNull(item.ActiveTimeout);
        Assert.Equal(50, item.ActiveTimeout.WarningThresholdPercent);
    }

    [Fact]
    public async Task ActiveTimeout_Is_Null_For_Item_Delivered_And_Flagged()
    {
        await SeedUsersAsync();
        await SeedTxAsync(TransactionStatus.ITEM_DELIVERED, sellerId: _seller.Id, buyerId: _buyer.Id);
        await SeedTxAsync(TransactionStatus.FLAGGED, sellerId: _seller.Id);

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Null(item.ActiveTimeout));
    }

    [Fact]
    public async Task ActiveTimeout_Is_Null_For_Completed_And_Cancelled()
    {
        await SeedUsersAsync();
        await SeedTxAsync(TransactionStatus.COMPLETED, sellerId: _seller.Id);
        await SeedTxAsync(TransactionStatus.CANCELLED_TIMEOUT, sellerId: _seller.Id);

        var completed = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Completed), default);
        var cancelled = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Cancelled), default);

        Assert.Null(Assert.Single(completed.Items).ActiveTimeout);
        Assert.Null(Assert.Single(cancelled.Items).ActiveTimeout);
    }

    [Fact]
    public async Task ActiveTimeout_Frozen_Uses_TimeoutRemainingSeconds()
    {
        await SeedUsersAsync();
        var tx = await SeedTxAsync(TransactionStatus.ITEM_ESCROWED, sellerId: _seller.Id, buyerId: _buyer.Id);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        tx.PaymentDeadline = nowUtc.AddHours(1); // would be 3600 live
        tx.TimeoutFrozenAt = nowUtc;
        tx.TimeoutFreezeReason = TimeoutFreezeReason.MAINTENANCE;
        tx.TimeoutRemainingSeconds = 1800; // frozen overrides live calc
        await _db.SaveChangesAsync();

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        var item = Assert.Single(result.Items);
        Assert.NotNull(item.ActiveTimeout);
        Assert.Equal(1800, item.ActiveTimeout.RemainingSeconds);
    }

    [Fact]
    public async Task ActiveTimeout_RemainingSeconds_Clamped_To_Zero_When_Deadline_Past()
    {
        await SeedUsersAsync();
        var tx = await SeedTxAsync(TransactionStatus.CREATED, sellerId: _seller.Id);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        tx.AcceptDeadline = nowUtc.AddMinutes(-5); // past
        await _db.SaveChangesAsync();

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        Assert.Equal(0, Assert.Single(result.Items).ActiveTimeout!.RemainingSeconds);
    }

    // ─── userRole resolver ─────────────────────────────────────────────────

    [Fact]
    public async Task UserRole_Resolves_To_Seller_When_Caller_Is_SellerId()
    {
        await SeedUsersAsync();
        await SeedTxAsync(TransactionStatus.CREATED, sellerId: _seller.Id);

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        Assert.Equal("seller", Assert.Single(result.Items).UserRole);
    }

    [Fact]
    public async Task UserRole_Resolves_To_Buyer_When_Caller_Is_BuyerId()
    {
        await SeedUsersAsync();
        await SeedTxAsync(TransactionStatus.ACCEPTED, sellerId: _seller.Id, buyerId: _buyer.Id);

        var result = await _sut.ListAsync(_buyer.Id, Query(TransactionListTab.Active), default);

        Assert.Equal("buyer", Assert.Single(result.Items).UserRole);
    }

    // ─── Counterparty resolution ───────────────────────────────────────────

    [Fact]
    public async Task Counterparty_Null_When_Buyer_Not_Set_Yet()
    {
        await SeedUsersAsync();
        // Seller-side view of a CREATED transaction (no buyer yet).
        await SeedTxAsync(TransactionStatus.CREATED, sellerId: _seller.Id);

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        Assert.Null(Assert.Single(result.Items).Counterparty);
    }

    [Fact]
    public async Task Counterparty_Populated_With_Buyer_For_Seller_View()
    {
        await SeedUsersAsync();
        await SeedTxAsync(TransactionStatus.ACCEPTED, sellerId: _seller.Id, buyerId: _buyer.Id);

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        var counterparty = Assert.Single(result.Items).Counterparty;
        Assert.NotNull(counterparty);
        Assert.Equal(_buyer.SteamId, counterparty.SteamId);
        Assert.Equal(_buyer.SteamDisplayName, counterparty.DisplayName);
    }

    [Fact]
    public async Task Counterparty_Populated_With_Seller_For_Buyer_View()
    {
        await SeedUsersAsync();
        await SeedTxAsync(TransactionStatus.ACCEPTED, sellerId: _seller.Id, buyerId: _buyer.Id);

        var result = await _sut.ListAsync(_buyer.Id, Query(TransactionListTab.Active), default);

        var counterparty = Assert.Single(result.Items).Counterparty;
        Assert.NotNull(counterparty);
        Assert.Equal(_seller.SteamId, counterparty.SteamId);
        Assert.Equal(_seller.SteamDisplayName, counterparty.DisplayName);
    }

    // ─── Ordering ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Items_Ordered_By_CreatedAt_Descending()
    {
        await SeedUsersAsync();
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        // BaseEntity sets CreatedAt on Add; we override after-save to control order.
        var older = await SeedTxAsync(TransactionStatus.CREATED, sellerId: _seller.Id);
        var newer = await SeedTxAsync(TransactionStatus.CREATED, sellerId: _seller.Id);
        older.CreatedAt = nowUtc.AddDays(-2);
        newer.CreatedAt = nowUtc.AddDays(-1);
        await _db.SaveChangesAsync();

        var result = await _sut.ListAsync(_seller.Id, Query(TransactionListTab.Active), default);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(newer.Id, result.Items[0].Id);
        Assert.Equal(older.Id, result.Items[1].Id);
    }

    // ─── Pagination clamping ───────────────────────────────────────────────

    [Theory]
    [InlineData(0, 20, 1, 20)]      // page < 1 → 1
    [InlineData(-5, 20, 1, 20)]     // negative page → 1
    [InlineData(1, 0, 1, 20)]       // pageSize < 1 → default 20
    [InlineData(1, 500, 1, 100)]    // pageSize > 100 → 100
    [InlineData(2, 50, 2, 50)]      // valid values pass through
    public async Task Pagination_Inputs_Are_Clamped_To_Safe_Range(
        int requestedPage, int requestedSize, int expectedPage, int expectedSize)
    {
        await SeedUsersAsync();

        var result = await _sut.ListAsync(_seller.Id, new TransactionListQuery(
            TransactionListTab.Active, requestedPage, requestedSize), default);

        Assert.Equal(expectedPage, result.Page);
        Assert.Equal(expectedSize, result.PageSize);
    }

    [Fact]
    public async Task Pagination_Returns_Distinct_Pages_Across_Calls()
    {
        await SeedUsersAsync();
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < 5; i++)
        {
            var tx = await SeedTxAsync(TransactionStatus.CREATED, sellerId: _seller.Id);
            tx.CreatedAt = nowUtc.AddMinutes(-i); // newest = i=0
        }
        await _db.SaveChangesAsync();

        var page1 = await _sut.ListAsync(_seller.Id, new TransactionListQuery(
            TransactionListTab.Active, Page: 1, PageSize: 2), default);
        var page2 = await _sut.ListAsync(_seller.Id, new TransactionListQuery(
            TransactionListTab.Active, Page: 2, PageSize: 2), default);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.DoesNotContain(page1.Items.Select(i => i.Id), id => page2.Items.Any(p => p.Id == id));
    }

    // ─── DTO serialization (07 §7.1 contract shape) ────────────────────────

    [Fact]
    public void Price_Serialized_As_String_With_Two_Decimals()
    {
        var dto = new TransactionListItemDto(
            Id: Guid.NewGuid(),
            ItemName: "AK-47 | Redline",
            ItemImageUrl: null,
            Status: "ITEM_ESCROWED",
            Price: 100.00m.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            Stablecoin: StablecoinType.USDT,
            Counterparty: null,
            UserRole: "seller",
            ActiveTimeout: null,
            CreatedAt: DateTime.UtcNow);

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        });

        Assert.Contains("\"price\":\"100.00\"", json);
        // ItemImageUrl + Counterparty + ActiveTimeout suppressed via WhenWritingNull.
        Assert.DoesNotContain("itemImageUrl", json);
        Assert.DoesNotContain("counterparty", json);
        Assert.DoesNotContain("activeTimeout", json);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static TransactionListQuery Query(TransactionListTab tab)
        => new(tab, Page: 1, PageSize: 20);

    private async Task SeedUsersAsync()
    {
        _seller = NewUser("76561198000000010", "SellerPlayer");
        _buyer = NewUser("76561198000000011", "BuyerPlayer");
        _other = NewUser("76561198000000012", "OtherPlayer");
        _db.Set<User>().AddRange(_seller, _buyer, _other);
        await _db.SaveChangesAsync();
    }

    private static User NewUser(string steamId, string displayName) => new()
    {
        Id = Guid.NewGuid(),
        SteamId = steamId,
        SteamDisplayName = displayName,
        SteamAvatarUrl = $"https://steamcdn.example/{steamId}.jpg",
        PreferredLanguage = "en",
        CreatedAt = DateTime.UtcNow.AddDays(-200),
    };

    private async Task<Transaction> SeedTxAsync(
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
            // CK_Transactions_Cancel — CANCELLED_* requires {CancelledBy,
            // CancelReason, CancelledAt} NOT NULL.
            CancelledAt = IsCancelled(status) ? nowUtc.AddMinutes(-1) : null,
            CancelledBy = IsCancelled(status) ? CancelledByType.BUYER : null,
            CancelReason = IsCancelled(status) ? "Test iptal sebebi (>=10 char)" : null,
            CompletedAt = status == TransactionStatus.COMPLETED ? nowUtc.AddMinutes(-1) : null,
        };
        _db.Set<Transaction>().Add(tx);
        await _db.SaveChangesAsync();
        return tx;
    }

    private static bool IsCancelled(TransactionStatus status) =>
        status is TransactionStatus.CANCELLED_TIMEOUT
            or TransactionStatus.CANCELLED_SELLER
            or TransactionStatus.CANCELLED_BUYER
            or TransactionStatus.CANCELLED_ADMIN;

    private void ApplyEmergencyHold(Transaction tx, Guid adminId)
    {
        // 06 §3.5 invariant trio: IsOnHold=1 ↔ EmergencyHold{At,Reason,ByAdmin}
        // NOT NULL ↔ TimeoutFrozenAt + Reason='EMERGENCY_HOLD' + RemainingSeconds NOT NULL.
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        tx.IsOnHold = true;
        tx.EmergencyHoldAt = nowUtc;
        tx.EmergencyHoldReason = "Sanctions match";
        tx.EmergencyHoldByAdminId = adminId;
        tx.PreviousStatusBeforeHold = (int)tx.Status;
        tx.TimeoutFrozenAt = nowUtc;
        tx.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        tx.TimeoutRemainingSeconds = 0;
    }
}
