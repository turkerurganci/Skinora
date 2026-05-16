using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Application.BotSelection;
using Skinora.Steam.Domain.Entities;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Steam.Tests.Integration;

/// <summary>
/// T69 — capacity-based bot selection against a real SQL Server. Validates
/// the ordering rules from 06 §3.10 / 05 §3.2: ACTIVE-only, lowest
/// ActiveEscrowCount first, oldest LastHealthCheckAt as tie-break, Id as
/// final tie-break.
/// </summary>
public class SqlBotSelectionServiceTests : IntegrationTestBase
{
    static SqlBotSelectionServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
    }

    protected override Task SeedAsync(AppDbContext context) => Task.CompletedTask;

    private SqlBotSelectionService CreateSut() => new(Context);

    [Fact]
    public async Task SelectAsync_PrefersLowestActiveEscrowCount()
    {
        var loaded = await CreateBotAsync("EscrowBot-Heavy", PlatformSteamBotStatus.ACTIVE, activeEscrowCount: 7);
        var light = await CreateBotAsync("EscrowBot-Light", PlatformSteamBotStatus.ACTIVE, activeEscrowCount: 1);
        await CreateBotAsync("EscrowBot-Mid", PlatformSteamBotStatus.ACTIVE, activeEscrowCount: 3);

        var picked = await CreateSut().SelectAsync(CancellationToken.None);

        Assert.NotNull(picked);
        Assert.Equal(light.Id, picked!.Id);
        Assert.NotEqual(loaded.Id, picked.Id);
    }

    [Fact]
    public async Task SelectAsync_SkipsRestrictedBannedAndOfflineBots()
    {
        await CreateBotAsync("EscrowBot-Restricted", PlatformSteamBotStatus.RESTRICTED, activeEscrowCount: 0);
        await CreateBotAsync("EscrowBot-Banned", PlatformSteamBotStatus.BANNED, activeEscrowCount: 0);
        await CreateBotAsync("EscrowBot-Offline", PlatformSteamBotStatus.OFFLINE, activeEscrowCount: 0);
        var only = await CreateBotAsync("EscrowBot-Active", PlatformSteamBotStatus.ACTIVE, activeEscrowCount: 5);

        var picked = await CreateSut().SelectAsync(CancellationToken.None);

        Assert.NotNull(picked);
        Assert.Equal(only.Id, picked!.Id);
    }

    [Fact]
    public async Task SelectAsync_SkipsSoftDeletedBots()
    {
        await CreateBotAsync(
            "EscrowBot-Deleted",
            PlatformSteamBotStatus.ACTIVE,
            activeEscrowCount: 0,
            isDeleted: true);
        var live = await CreateBotAsync("EscrowBot-Live", PlatformSteamBotStatus.ACTIVE, activeEscrowCount: 4);

        var picked = await CreateSut().SelectAsync(CancellationToken.None);

        Assert.NotNull(picked);
        Assert.Equal(live.Id, picked!.Id);
    }

    [Fact]
    public async Task SelectAsync_TiesBrokenByOldestLastHealthCheck()
    {
        // Both ACTIVE with equal ActiveEscrowCount: the one probed earliest
        // should win (least recently used → distribute load).
        var older = await CreateBotAsync(
            "EscrowBot-Older",
            PlatformSteamBotStatus.ACTIVE,
            activeEscrowCount: 2,
            lastHealthCheck: DateTime.UtcNow.AddMinutes(-30));
        await CreateBotAsync(
            "EscrowBot-Newer",
            PlatformSteamBotStatus.ACTIVE,
            activeEscrowCount: 2,
            lastHealthCheck: DateTime.UtcNow.AddSeconds(-10));

        var picked = await CreateSut().SelectAsync(CancellationToken.None);

        Assert.NotNull(picked);
        Assert.Equal(older.Id, picked!.Id);
    }

    [Fact]
    public async Task SelectAsync_ReturnsNullWhenNoActiveBots()
    {
        await CreateBotAsync("EscrowBot-Restricted-Only", PlatformSteamBotStatus.RESTRICTED, activeEscrowCount: 0);

        var picked = await CreateSut().SelectAsync(CancellationToken.None);

        Assert.Null(picked);
    }

    private async Task<PlatformSteamBot> CreateBotAsync(
        string displayName,
        PlatformSteamBotStatus status,
        int activeEscrowCount,
        bool isDeleted = false,
        DateTime? lastHealthCheck = null)
    {
        var bot = new PlatformSteamBot
        {
            Id = Guid.NewGuid(),
            SteamId = $"7656119{Guid.NewGuid().ToString("N").Substring(0, 10)}",
            DisplayName = displayName,
            Status = status,
            ActiveEscrowCount = activeEscrowCount,
            DailyTradeOfferCount = 0,
            LastHealthCheckAt = lastHealthCheck,
            IsDeleted = isDeleted,
        };
        Context.Set<PlatformSteamBot>().Add(bot);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return bot;
    }
}
