using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Platform.Tests.Integration;

/// <summary>
/// EF-backed coverage for <see cref="AuditLogQueryService"/> (T42, AD18).
/// Exercises every filter (category, dateFrom/dateTo, search, transactionId)
/// plus pagination and actor/subject hydration.
/// </summary>
public class AuditLogQueryServiceTests : IntegrationTestBase
{
    static AuditLogQueryServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private User _admin = null!;
    private User _subject = null!;
    private Guid _transactionId;

    protected override async Task SeedAsync(AppDbContext context)
    {
        // SeedConstants.SystemUserId is inserted by UserConfiguration HasData
        // (06 §8.9) when EnsureCreatedAsync builds the schema.
        _admin = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198555000700",
            SteamDisplayName = "QueryAdmin",
        };
        _subject = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198555000701",
            SteamDisplayName = "BannedUser",
        };
        context.Set<User>().Add(_admin);
        context.Set<User>().Add(_subject);
        _transactionId = Guid.NewGuid();
        await context.SaveChangesAsync();

        // Seed a deterministic spread:
        //   - 1 SECURITY_EVENT (today)
        //   - 1 ADMIN_ACTION USER_BANNED on _subject (today)
        //   - 1 ADMIN_ACTION SYSTEM_SETTING_CHANGED for "commission_rate" (yesterday)
        //   - 1 FUND_MOVEMENT WALLET_REFUND on transaction (last week)
        //   - 1 FUND_MOVEMENT WALLET_DEPOSIT on different transaction (last week)
        var today = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        context.Set<AuditLog>().AddRange(
            new AuditLog
            {
                UserId = _admin.Id,
                ActorId = _admin.Id,
                ActorType = ActorType.ADMIN,
                Action = AuditAction.WALLET_ADDRESS_CHANGED,
                EntityType = "User",
                EntityId = _admin.Id.ToString(),
                NewValue = "{\"address\":\"TXabc\"}",
                CreatedAt = today,
            },
            new AuditLog
            {
                UserId = _subject.Id,
                ActorId = _admin.Id,
                ActorType = ActorType.ADMIN,
                Action = AuditAction.USER_BANNED,
                EntityType = "User",
                EntityId = _subject.Id.ToString(),
                NewValue = "{\"reason\":\"sanctions\"}",
                CreatedAt = today,
            },
            new AuditLog
            {
                UserId = _admin.Id,
                ActorId = _admin.Id,
                ActorType = ActorType.ADMIN,
                Action = AuditAction.SYSTEM_SETTING_CHANGED,
                EntityType = "SystemSetting",
                EntityId = "commission_rate",
                NewValue = "{\"value\":\"0.03\"}",
                CreatedAt = today.AddDays(-1),
            },
            new AuditLog
            {
                UserId = null,
                ActorId = SeedConstants.SystemUserId,
                ActorType = ActorType.SYSTEM,
                Action = AuditAction.WALLET_REFUND,
                EntityType = "Transaction",
                EntityId = _transactionId.ToString(),
                NewValue = "{\"amount\":\"5.00\"}",
                CreatedAt = today.AddDays(-7),
            },
            new AuditLog
            {
                UserId = null,
                ActorId = SeedConstants.SystemUserId,
                ActorType = ActorType.SYSTEM,
                Action = AuditAction.WALLET_DEPOSIT,
                EntityType = "Transaction",
                EntityId = Guid.NewGuid().ToString(),
                NewValue = "not-json",
                CreatedAt = today.AddDays(-7),
            });
        await context.SaveChangesAsync();
    }

    private AuditLogQueryService CreateService() => new(Context);

    private static AuditLogListQuery DefaultQuery(
        string? category = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        string? search = null,
        Guid? transactionId = null,
        int page = 1,
        int pageSize = 20) =>
        new(category, dateFrom, dateTo, search, transactionId, page, pageSize);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_NoFilters_Returns_All_Rows_Newest_First()
    {
        var service = CreateService();

        var result = await service.ListAsync(DefaultQuery(), CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(5, result.Items.Count);
        // Newest-first: Ids are monotonically increasing, so descending IDs
        // imply descending insertion order. Verify ordering rather than the
        // category at a specific slot (insertion order is fragile).
        var ids = result.Items.Select(i => long.Parse(i.Id)).ToList();
        Assert.Equal(ids.OrderByDescending(x => x).ToList(), ids);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_Category_FUND_MOVEMENT_Filters_To_Two()
    {
        var service = CreateService();

        var result = await service.ListAsync(
            DefaultQuery(category: AuditLogCategoryMap.Categories.FundMovement),
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item =>
            Assert.Equal(AuditLogCategoryMap.Categories.FundMovement, item.Category));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_Unknown_Category_Returns_Empty_Without_Error()
    {
        var service = CreateService();

        var result = await service.ListAsync(
            DefaultQuery(category: "BOGUS"), CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_DateFrom_Filters_Recent_Rows_Only()
    {
        var service = CreateService();

        var result = await service.ListAsync(
            DefaultQuery(dateFrom: new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        // Today's two entries + yesterday's SYSTEM_SETTING_CHANGED.
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_DateRange_Restricts_To_Window()
    {
        var service = CreateService();

        var result = await service.ListAsync(
            DefaultQuery(
                dateFrom: new DateTime(2026, 4, 25, 0, 0, 0, DateTimeKind.Utc),
                dateTo: new DateTime(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc)),
            CancellationToken.None);

        // Only yesterday's entry falls inside the window.
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(nameof(AuditAction.SYSTEM_SETTING_CHANGED), result.Items[0].Action);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_Search_Matches_EntityId_Substring()
    {
        var service = CreateService();

        var result = await service.ListAsync(
            DefaultQuery(search: "commission_rate"), CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(nameof(AuditAction.SYSTEM_SETTING_CHANGED), result.Items[0].Action);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_TransactionId_Matches_EntityType_Transaction_Only()
    {
        var service = CreateService();

        var result = await service.ListAsync(
            DefaultQuery(transactionId: _transactionId), CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(nameof(AuditAction.WALLET_REFUND), result.Items[0].Action);
        Assert.Equal(_transactionId, result.Items[0].TransactionId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_Hydrates_Actor_And_Subject_DisplayNames()
    {
        var service = CreateService();

        var result = await service.ListAsync(
            DefaultQuery(category: AuditLogCategoryMap.Categories.AdminAction),
            CancellationToken.None);

        var banned = result.Items.Single(i => i.Action == nameof(AuditAction.USER_BANNED));
        Assert.Equal("QueryAdmin", banned.Actor.DisplayName);
        Assert.Equal("76561198555000700", banned.Actor.SteamId);
        Assert.NotNull(banned.Subject);
        Assert.Equal("BannedUser", banned.Subject!.DisplayName);
        Assert.Equal("76561198555000701", banned.Subject.SteamId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_System_Actor_Surfaces_Display_Name_System_Without_SteamId()
    {
        var service = CreateService();

        var result = await service.ListAsync(
            DefaultQuery(category: AuditLogCategoryMap.Categories.FundMovement),
            CancellationToken.None);

        Assert.All(result.Items, item =>
        {
            Assert.Equal("System", item.Actor.DisplayName);
            Assert.Null(item.Actor.SteamId);
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_NonJson_NewValue_Surfaces_As_String_Detail()
    {
        var service = CreateService();

        var result = await service.ListAsync(
            DefaultQuery(category: AuditLogCategoryMap.Categories.FundMovement),
            CancellationToken.None);

        var deposit = result.Items.Single(i => i.Action == nameof(AuditAction.WALLET_DEPOSIT));
        Assert.NotNull(deposit.Detail);
        Assert.Equal("not-json", deposit.Detail!.Value.GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_Pagination_Splits_Pages_And_Reports_Total()
    {
        var service = CreateService();

        var page1 = await service.ListAsync(
            DefaultQuery(page: 1, pageSize: 2), CancellationToken.None);
        var page2 = await service.ListAsync(
            DefaultQuery(page: 2, pageSize: 2), CancellationToken.None);
        var page3 = await service.ListAsync(
            DefaultQuery(page: 3, pageSize: 2), CancellationToken.None);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);

        var seen = page1.Items.Select(i => i.Id)
            .Concat(page2.Items.Select(i => i.Id))
            .Concat(page3.Items.Select(i => i.Id))
            .ToList();
        Assert.Equal(seen.Distinct().Count(), seen.Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_Caps_Oversize_PageSize()
    {
        var service = CreateService();

        var result = await service.ListAsync(
            DefaultQuery(pageSize: 9999), CancellationToken.None);

        Assert.Equal(100, result.PageSize);
    }
}
