using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Domain.Calculations;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.GasFee;

/// <summary>
/// Integration coverage for <see cref="GasFeeSettingsProvider"/> — verifies
/// the live SystemSetting read path (06 §3.17 / 02 §4.7 / 09 §14.4) using
/// the shared SQL Server fixture (T11.3).
/// </summary>
[Trait("Category", "Integration")]
public class GasFeeSettingsProviderTests : IntegrationTestBase
{
    static GasFeeSettingsProviderTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private GasFeeSettingsProvider CreateSut() => new(Context);

    private async Task SetSettingAsync(string key, string? value, bool isConfigured = true)
    {
        var existing = await Context.Set<SystemSetting>()
            .SingleOrDefaultAsync(s => s.Key == key);

        if (existing is null)
        {
            Context.Set<SystemSetting>().Add(new SystemSetting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value,
                IsConfigured = isConfigured,
                DataType = "decimal",
                Category = "test",
                Description = "test",
            });
        }
        else
        {
            existing.Value = value;
            existing.IsConfigured = isConfigured;
        }
        await Context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAsync_SeededValues_ReturnsBoth()
    {
        await SetSettingAsync(GasFeeSettingsProvider.ProtectionRatioKey, "0.10");
        await SetSettingAsync(GasFeeSettingsProvider.MinRefundThresholdRatioKey, "2.0");

        var settings = await CreateSut().GetAsync(default);

        Assert.Equal(0.10m, settings.ProtectionRatio);
        Assert.Equal(2.0m, settings.MinRefundThresholdRatio);
    }

    [Fact]
    public async Task GetAsync_AdminCustomisedValues_AreReadLive()
    {
        // Admin updates both settings — provider should return the new pair
        // immediately (no caching layer).
        await SetSettingAsync(GasFeeSettingsProvider.ProtectionRatioKey, "0.25");
        await SetSettingAsync(GasFeeSettingsProvider.MinRefundThresholdRatioKey, "3.5");

        var settings = await CreateSut().GetAsync(default);

        Assert.Equal(0.25m, settings.ProtectionRatio);
        Assert.Equal(3.5m, settings.MinRefundThresholdRatio);
    }

    [Fact]
    public async Task GetAsync_MissingProtectionRatio_FallsBackToDefault()
    {
        // Mark the row unconfigured — IsConfigured filter excludes it.
        await SetSettingAsync(GasFeeSettingsProvider.ProtectionRatioKey, value: null, isConfigured: false);
        await SetSettingAsync(GasFeeSettingsProvider.MinRefundThresholdRatioKey, "2.0");

        var settings = await CreateSut().GetAsync(default);

        Assert.Equal(FinancialCalculator.DefaultGasFeeProtectionRatio, settings.ProtectionRatio);
        Assert.Equal(2.0m, settings.MinRefundThresholdRatio);
    }

    [Fact]
    public async Task GetAsync_MalformedProtectionRatio_FallsBackToDefault()
    {
        // Out-of-range value (validator stage 2 enforces 0 < ratio < 1) —
        // the read side mirrors that envelope so a poisoned row cannot
        // collapse the seller protection threshold.
        await SetSettingAsync(GasFeeSettingsProvider.ProtectionRatioKey, "1.5");
        await SetSettingAsync(GasFeeSettingsProvider.MinRefundThresholdRatioKey, "2.0");

        var settings = await CreateSut().GetAsync(default);

        Assert.Equal(FinancialCalculator.DefaultGasFeeProtectionRatio, settings.ProtectionRatio);
    }

    [Fact]
    public async Task GetAsync_MalformedMinRefundRatio_FallsBackToDefault()
    {
        await SetSettingAsync(GasFeeSettingsProvider.ProtectionRatioKey, "0.10");
        await SetSettingAsync(GasFeeSettingsProvider.MinRefundThresholdRatioKey, "abc");

        var settings = await CreateSut().GetAsync(default);

        Assert.Equal(FinancialCalculator.DefaultMinimumRefundThresholdRatio, settings.MinRefundThresholdRatio);
    }

    [Fact]
    public async Task GetAsync_BothRowsAbsent_FallsBackToDefaults()
    {
        // Marking both unconfigured simulates a partial-seed environment.
        await SetSettingAsync(GasFeeSettingsProvider.ProtectionRatioKey, null, isConfigured: false);
        await SetSettingAsync(GasFeeSettingsProvider.MinRefundThresholdRatioKey, null, isConfigured: false);

        var settings = await CreateSut().GetAsync(default);

        Assert.Equal(FinancialCalculator.DefaultGasFeeProtectionRatio, settings.ProtectionRatio);
        Assert.Equal(FinancialCalculator.DefaultMinimumRefundThresholdRatio, settings.MinRefundThresholdRatio);
    }
}
