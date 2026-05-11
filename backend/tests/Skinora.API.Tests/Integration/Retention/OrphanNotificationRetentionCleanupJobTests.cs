using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.API.Retention;
using Skinora.Notifications.Domain.Entities;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.API.Tests.Integration.Retention;

/// <summary>
/// T63b — OrphanNotificationRetentionCleanupJob coverage. Confirms the
/// sweep deletes only orphan notifications (TransactionId IS NULL) past the
/// retention window along with their NotificationDelivery rows
/// (delivery → notification order, FK preserved), leaves transaction-bound
/// notifications alone, and reads the retention window + batch size from
/// SystemSettings (06 §1, §6.1).
/// </summary>
public class OrphanNotificationRetentionCleanupJobTests : IntegrationTestBase
{
    static OrphanNotificationRetentionCleanupJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
        NotificationsModuleDbRegistration.RegisterNotificationsModule();
    }

    private Guid _userId;

    protected override async Task SeedAsync(AppDbContext context)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000000",
            SteamDisplayName = "Test",
        };
        context.Set<User>().Add(user);
        await context.SaveChangesAsync();
        _userId = user.Id;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Old_Orphan_Notification_And_Its_Deliveries_Are_Purged()
    {
        var stale = await SeedOrphanNotificationAsync(DateTime.UtcNow.AddDays(-400));
        await SeedDeliveryAsync(stale, NotificationChannel.EMAIL);
        await SeedDeliveryAsync(stale, NotificationChannel.TELEGRAM);

        var sut = NewJob();
        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, result.NotificationsDeleted);
        Assert.Equal(2, result.DeliveriesDeleted);

        Assert.Empty(await Context.Set<Notification>().IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Empty(await Context.Set<NotificationDelivery>().AsNoTracking().ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Transaction_Bound_Notifications_Are_Not_Touched()
    {
        var orphan = await SeedOrphanNotificationAsync(DateTime.UtcNow.AddDays(-400));
        var txBound = await SeedNotificationAsync(
            createdAt: DateTime.UtcNow.AddDays(-400), transactionId: Guid.NewGuid());

        var sut = NewJob();
        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, result.NotificationsDeleted);

        var remaining = await Context.Set<Notification>().IgnoreQueryFilters().AsNoTracking()
            .Select(n => n.Id).ToListAsync();
        Assert.DoesNotContain(orphan, remaining);
        Assert.Contains(txBound, remaining);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Fresh_Orphans_Are_Preserved()
    {
        await SeedOrphanNotificationAsync(DateTime.UtcNow.AddDays(-30));

        var sut = NewJob();
        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(0, result.NotificationsDeleted);
        Assert.Single(await Context.Set<Notification>().IgnoreQueryFilters().AsNoTracking().ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Soft_Deleted_Orphans_Past_Threshold_Are_Hard_Purged()
    {
        var id = await SeedOrphanNotificationAsync(
            DateTime.UtcNow.AddDays(-400), softDeleted: true);

        var sut = NewJob();
        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, result.NotificationsDeleted);
        var remaining = await Context.Set<Notification>().IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.Id == id).ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Batch_Loop_Drains_All_Eligible_Rows()
    {
        // 8 eligible orphans + batch size 3 → 3 iterations.
        for (var i = 0; i < 8; i++)
        {
            await SeedOrphanNotificationAsync(DateTime.UtcNow.AddDays(-400 - i));
        }

        await OverrideSettingAsync(OrphanNotificationRetentionCleanupJob.BatchSizeKey, "3");

        var sut = NewJob();
        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(8, result.NotificationsDeleted);
        Assert.Empty(await Context.Set<Notification>().IgnoreQueryFilters().AsNoTracking().ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemSetting_Override_Shortens_Retention_Window()
    {
        // 40 days old: survives default 365d window, but caught by override = 30d.
        await SeedOrphanNotificationAsync(DateTime.UtcNow.AddDays(-40));

        var defaultRun = await NewJob().ExecuteAsync(CancellationToken.None);
        Assert.Equal(0, defaultRun.NotificationsDeleted);

        await OverrideSettingAsync(OrphanNotificationRetentionCleanupJob.RetentionDaysKey, "30");

        var shortRun = await NewJob().ExecuteAsync(CancellationToken.None);
        Assert.Equal(1, shortRun.NotificationsDeleted);
    }

    private OrphanNotificationRetentionCleanupJob NewJob() =>
        new(Context, NullLogger<OrphanNotificationRetentionCleanupJob>.Instance);

    private Task<Guid> SeedOrphanNotificationAsync(DateTime createdAt, bool softDeleted = false) =>
        SeedNotificationAsync(createdAt, transactionId: null, softDeleted);

    private async Task<Guid> SeedNotificationAsync(
        DateTime createdAt, Guid? transactionId, bool softDeleted = false)
    {
        // Notification implements IAuditableEntity — UpdateAuditFields overwrites
        // CreatedAt on Added state. Two-step save: insert with default audit
        // pipeline, then update CreatedAt (modified state pipeline only touches
        // UpdatedAt). Same approach used by T33+ tests.
        var n = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            TransactionId = transactionId,
            Type = NotificationType.ADMIN_FLAG_ALERT,
            Title = "t",
            Body = "b",
            IsDeleted = softDeleted,
            DeletedAt = softDeleted ? createdAt : null,
        };
        Context.Set<Notification>().Add(n);
        await Context.SaveChangesAsync();

        n.CreatedAt = createdAt;
        await Context.SaveChangesAsync();
        return n.Id;
    }

    private async Task SeedDeliveryAsync(Guid notificationId, NotificationChannel channel)
    {
        var d = new NotificationDelivery
        {
            Id = Guid.NewGuid(),
            NotificationId = notificationId,
            Channel = channel,
            TargetExternalId = "external",
            Status = DeliveryStatus.PENDING,
            AttemptCount = 0,
        };
        Context.Set<NotificationDelivery>().Add(d);
        await Context.SaveChangesAsync();
    }

    private async Task OverrideSettingAsync(string key, string value)
    {
        var setting = await Context.Set<SystemSetting>().SingleAsync(s => s.Key == key);
        setting.Value = value;
        setting.IsConfigured = true;
        await Context.SaveChangesAsync();
    }
}
