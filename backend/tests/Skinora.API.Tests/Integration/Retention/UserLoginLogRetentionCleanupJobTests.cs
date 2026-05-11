using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.API.Retention;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.API.Tests.Integration.Retention;

/// <summary>
/// T63b — UserLoginLogRetentionCleanupJob coverage. Verifies that the sweep
/// hard-purges <see cref="UserLoginLog"/> rows past the retention window
/// regardless of their soft-delete state, preserves fresh rows, drains
/// across batch boundaries and honours the SystemSetting override
/// (06 §1, §6.1).
/// </summary>
public class UserLoginLogRetentionCleanupJobTests : IntegrationTestBase
{
    static UserLoginLogRetentionCleanupJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private Guid _userId;

    protected override async Task SeedAsync(AppDbContext context)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000001",
            SteamDisplayName = "Test",
        };
        context.Set<User>().Add(user);
        await context.SaveChangesAsync();
        _userId = user.Id;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Old_Login_Logs_Are_Hard_Deleted()
    {
        var stale = await SeedLogAsync(DateTime.UtcNow.AddDays(-400));
        var fresh = await SeedLogAsync(DateTime.UtcNow.AddDays(-30));

        var deleted = await NewJob().ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, deleted);

        var remaining = await Context.Set<UserLoginLog>().IgnoreQueryFilters()
            .AsNoTracking().Select(l => l.Id).ToListAsync();
        Assert.DoesNotContain(stale, remaining);
        Assert.Contains(fresh, remaining);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Soft_Deleted_Stale_Logs_Are_Also_Purged()
    {
        var id = await SeedLogAsync(DateTime.UtcNow.AddDays(-400), softDeleted: true);

        var deleted = await NewJob().ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        var remaining = await Context.Set<UserLoginLog>().IgnoreQueryFilters()
            .AsNoTracking().Where(l => l.Id == id).ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Batch_Loop_Drains_All_Eligible_Rows()
    {
        for (var i = 0; i < 7; i++)
        {
            await SeedLogAsync(DateTime.UtcNow.AddDays(-400 - i));
        }

        await OverrideSettingAsync(UserLoginLogRetentionCleanupJob.BatchSizeKey, "2");

        var deleted = await NewJob().ExecuteAsync(CancellationToken.None);

        Assert.Equal(7, deleted);
        Assert.Empty(await Context.Set<UserLoginLog>().IgnoreQueryFilters().AsNoTracking().ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemSetting_Override_Shortens_Retention_Window()
    {
        await SeedLogAsync(DateTime.UtcNow.AddDays(-90));

        Assert.Equal(0, await NewJob().ExecuteAsync(CancellationToken.None));

        await OverrideSettingAsync(UserLoginLogRetentionCleanupJob.RetentionDaysKey, "30");

        Assert.Equal(1, await NewJob().ExecuteAsync(CancellationToken.None));
    }

    private UserLoginLogRetentionCleanupJob NewJob() =>
        new(Context, NullLogger<UserLoginLogRetentionCleanupJob>.Instance);

    private async Task<long> SeedLogAsync(DateTime createdAt, bool softDeleted = false)
    {
        var log = new UserLoginLog
        {
            UserId = _userId,
            IpAddress = "127.0.0.1",
            CreatedAt = createdAt,
            IsDeleted = softDeleted,
            DeletedAt = softDeleted ? createdAt : null,
        };
        Context.Set<UserLoginLog>().Add(log);
        await Context.SaveChangesAsync();
        return log.Id;
    }

    private async Task OverrideSettingAsync(string key, string value)
    {
        var setting = await Context.Set<SystemSetting>().SingleAsync(s => s.Key == key);
        setting.Value = value;
        setting.IsConfigured = true;
        await Context.SaveChangesAsync();
    }
}
