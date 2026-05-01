using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Application.Settings;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Platform.Tests.Integration;

/// <summary>
/// EF-backed integration tests for <see cref="SystemSettingsService"/> (T41,
/// 07 §9.8–§9.9). Uses the shared <see cref="IntegrationTestBase"/> SQL Server
/// fixture so the audit-log INSERT round-trips through the production
/// <c>AppDbContext.EnforceAppendOnly</c> guard.
/// </summary>
public class SystemSettingsServiceTests : IntegrationTestBase
{
    static SystemSettingsServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private User _admin = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _admin = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198555000041",
            SteamDisplayName = "T41Admin",
        };
        context.Set<User>().Add(_admin);
        await context.SaveChangesAsync();
    }

    private SystemSettingsService CreateService(AppDbContext? ctx = null) =>
        new(ctx ?? Context, TimeProvider.System);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_Returns_Catalog_Entries_Mapped_To_Seed_Values()
    {
        var service = CreateService();

        var response = await service.ListAsync(CancellationToken.None);

        Assert.Equal(SystemSettingsCatalog.All.Count, response.Settings.Count);

        var commission = Assert.Single(response.Settings, s => s.Key == "commission_rate");
        Assert.Equal("0.02", commission.Value);
        Assert.Equal("commission", commission.Category);
        Assert.Equal("number", commission.ValueType);
        Assert.False(string.IsNullOrEmpty(commission.Label));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_Configured_Setting_Updates_Value_And_Writes_Audit()
    {
        var service = CreateService();

        var outcome = await service.UpdateAsync(
            "commission_rate",
            new UpdateSettingRequest("0.03"),
            _admin.Id,
            ipAddress: "10.0.0.1",
            CancellationToken.None);

        var success = Assert.IsType<UpdateSettingOutcome.Success>(outcome);
        Assert.Equal("0.03", success.Response.Value);

        await using var readCtx = CreateContext();
        var setting = await readCtx.Set<SystemSetting>()
            .FirstAsync(s => s.Key == "commission_rate");
        Assert.Equal("0.03", setting.Value);
        Assert.True(setting.IsConfigured);
        Assert.Equal(_admin.Id, setting.UpdatedByAdminId);

        var audit = await readCtx.Set<AuditLog>()
            .Where(a => a.EntityType == nameof(SystemSetting) && a.EntityId == "commission_rate")
            .OrderByDescending(a => a.Id)
            .FirstAsync();
        Assert.Equal(AuditAction.SYSTEM_SETTING_CHANGED, audit.Action);
        Assert.Equal(ActorType.ADMIN, audit.ActorType);
        Assert.Equal(_admin.Id, audit.ActorId);
        Assert.Equal("10.0.0.1", audit.IpAddress);
        Assert.Contains("0.02", audit.OldValue);
        Assert.Contains("0.03", audit.NewValue);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_Unconfigured_Setting_Hydrates_And_Marks_Configured()
    {
        var service = CreateService();

        var outcome = await service.UpdateAsync(
            "accept_timeout_minutes",
            new UpdateSettingRequest("60"),
            _admin.Id,
            ipAddress: null,
            CancellationToken.None);

        Assert.IsType<UpdateSettingOutcome.Success>(outcome);

        await using var readCtx = CreateContext();
        var setting = await readCtx.Set<SystemSetting>()
            .FirstAsync(s => s.Key == "accept_timeout_minutes");
        Assert.Equal("60", setting.Value);
        Assert.True(setting.IsConfigured);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_Unknown_Key_Returns_NotFound()
    {
        var service = CreateService();

        var outcome = await service.UpdateAsync(
            "made_up_setting",
            new UpdateSettingRequest("1"),
            _admin.Id,
            ipAddress: null,
            CancellationToken.None);

        Assert.IsType<UpdateSettingOutcome.NotFound>(outcome);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_BadType_Returns_ValidationFailed_And_No_Audit_Written()
    {
        var service = CreateService();

        var outcome = await service.UpdateAsync(
            "commission_rate",
            new UpdateSettingRequest("not-a-decimal"),
            _admin.Id,
            ipAddress: null,
            CancellationToken.None);

        var failed = Assert.IsType<UpdateSettingOutcome.ValidationFailed>(outcome);
        Assert.Contains("decimal", failed.Message);

        await using var readCtx = CreateContext();
        var auditCount = await readCtx.Set<AuditLog>()
            .CountAsync(a => a.EntityId == "commission_rate");
        Assert.Equal(0, auditCount);
        var commission = await readCtx.Set<SystemSetting>()
            .FirstAsync(s => s.Key == "commission_rate");
        Assert.Equal("0.02", commission.Value);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_OutOfRange_Ratio_Returns_ValidationFailed()
    {
        var service = CreateService();

        var outcome = await service.UpdateAsync(
            "commission_rate",
            new UpdateSettingRequest("1.5"),
            _admin.Id,
            ipAddress: null,
            CancellationToken.None);

        var failed = Assert.IsType<UpdateSettingOutcome.ValidationFailed>(outcome);
        Assert.Contains("commission_rate", failed.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_CrossKey_Violation_Returns_ValidationFailed()
    {
        var service = CreateService();

        // Hydrate min + default first, then attempt to set max BELOW min.
        await service.UpdateAsync("payment_timeout_min_minutes",
            new UpdateSettingRequest("30"), _admin.Id, null, CancellationToken.None);
        await service.UpdateAsync("payment_timeout_default_minutes",
            new UpdateSettingRequest("45"), _admin.Id, null, CancellationToken.None);
        await service.UpdateAsync("payment_timeout_max_minutes",
            new UpdateSettingRequest("60"), _admin.Id, null, CancellationToken.None);

        // Now attempt to push min above max.
        var outcome = await service.UpdateAsync(
            "payment_timeout_min_minutes",
            new UpdateSettingRequest("90"),
            _admin.Id,
            ipAddress: null,
            CancellationToken.None);

        var failed = Assert.IsType<UpdateSettingOutcome.ValidationFailed>(outcome);
        Assert.Contains("payment_timeout_min_minutes", failed.Message);
    }
}
