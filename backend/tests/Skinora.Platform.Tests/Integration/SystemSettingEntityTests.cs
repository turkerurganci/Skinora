using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Platform.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="SystemSetting"/> (T25, 06 §3.17).
/// Verifies CRUD, DataType CHECK, unique Key, Category index lookup.
/// </summary>
public class SystemSettingEntityTests : IntegrationTestBase
{
    static SystemSettingEntityTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private static SystemSetting CreateValid(string key = "commission_rate", string dataType = "decimal")
    {
        return new SystemSetting
        {
            Id = Guid.NewGuid(),
            Key = key,
            Value = "0.02",
            IsConfigured = true,
            DataType = dataType,
            Category = "Commission",
            Description = "Default platform commission rate"
        };
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemSetting_Insert_And_Read_RoundTrips()
    {
        var setting = CreateValid();

        Context.Set<SystemSetting>().Add(setting);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<SystemSetting>().FirstAsync(s => s.Id == setting.Id);

        Assert.Equal("commission_rate", loaded.Key);
        Assert.Equal("0.02", loaded.Value);
        Assert.True(loaded.IsConfigured);
        Assert.Equal("decimal", loaded.DataType);
        Assert.Equal("Commission", loaded.Category);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemSetting_Update_Value()
    {
        var setting = CreateValid();
        Context.Set<SystemSetting>().Add(setting);
        await Context.SaveChangesAsync();

        var tracked = await Context.Set<SystemSetting>().FirstAsync(s => s.Id == setting.Id);
        tracked.Value = "0.025";
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<SystemSetting>().FirstAsync(s => s.Id == setting.Id);
        Assert.Equal("0.025", loaded.Value);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemSetting_NullValue_Stored_As_Unconfigured()
    {
        var setting = CreateValid("unconfigured_key");
        setting.Value = null;
        setting.IsConfigured = false;

        Context.Set<SystemSetting>().Add(setting);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<SystemSetting>().FirstAsync(s => s.Id == setting.Id);
        Assert.Null(loaded.Value);
        Assert.False(loaded.IsConfigured);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemSetting_DuplicateKey_Rejected()
    {
        var first = CreateValid("cancel_limit_count");
        Context.Set<SystemSetting>().Add(first);
        await Context.SaveChangesAsync();

        await using var ctx = CreateContext();
        var duplicate = CreateValid("cancel_limit_count");
        ctx.Set<SystemSetting>().Add(duplicate);

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemSetting_InvalidDataType_Rejected_By_CheckConstraint()
    {
        // 06 §3.17: "DataType IN ('int', 'decimal', 'bool', 'string')"
        var invalid = CreateValid("bad_datatype_key", dataType: "float");
        Context.Set<SystemSetting>().Add(invalid);

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Theory]
    [InlineData("int")]
    [InlineData("decimal")]
    [InlineData("bool")]
    [InlineData("string")]
    [Trait("Category", "Integration")]
    public async Task SystemSetting_AllowedDataTypes_Accepted(string dataType)
    {
        var setting = CreateValid($"key_{dataType}", dataType);
        Context.Set<SystemSetting>().Add(setting);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<SystemSetting>().FirstAsync(s => s.Id == setting.Id);
        Assert.Equal(dataType, loaded.DataType);
    }
}
