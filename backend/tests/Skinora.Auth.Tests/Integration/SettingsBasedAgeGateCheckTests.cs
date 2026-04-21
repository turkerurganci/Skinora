using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Auth.Tests.Integration;

public class SettingsBasedAgeGateCheckTests : IntegrationTestBase
{
    static SettingsBasedAgeGateCheckTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private readonly FakeTimeProvider _clock = new(
        new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.Zero));

    private SettingsBasedAgeGateCheck CreateSut() =>
        new(Context, _clock, NullLogger<SettingsBasedAgeGateCheck>.Instance);

    private async Task SetThresholdAsync(int days)
    {
        var setting = new SystemSetting
        {
            Id = Guid.NewGuid(),
            Key = SettingsBasedAgeGateCheck.SettingKey,
            Value = days.ToString(),
            IsConfigured = true,
            DataType = "int",
            Category = "AccessControl",
            Description = "test",
        };
        Context.Set<SystemSetting>().Add(setting);
        await Context.SaveChangesAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EvaluateAsync_NullCreatedAt_ReturnsAllowed()
    {
        await SetThresholdAsync(30);

        var decision = await CreateSut().EvaluateAsync(null, default);

        Assert.False(decision.IsBlocked);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EvaluateAsync_FreshAccount_BelowThreshold_ReturnsBlocked()
    {
        await SetThresholdAsync(30);
        var created = _clock.GetUtcNow().UtcDateTime.AddDays(-5);

        var decision = await CreateSut().EvaluateAsync(created, default);

        Assert.True(decision.IsBlocked);
        Assert.Equal(5, decision.AccountAgeDays);
        Assert.Equal(30, decision.RequiredDays);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EvaluateAsync_OldAccount_AboveThreshold_ReturnsAllowed()
    {
        await SetThresholdAsync(30);
        var created = _clock.GetUtcNow().UtcDateTime.AddDays(-100);

        var decision = await CreateSut().EvaluateAsync(created, default);

        Assert.False(decision.IsBlocked);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EvaluateAsync_ThresholdZero_ReturnsAllowed()
    {
        await SetThresholdAsync(0);
        var created = _clock.GetUtcNow().UtcDateTime.AddDays(-1);

        var decision = await CreateSut().EvaluateAsync(created, default);

        Assert.False(decision.IsBlocked);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EvaluateAsync_SettingMissing_ReturnsAllowed()
    {
        // No SystemSetting row inserted.
        var created = _clock.GetUtcNow().UtcDateTime.AddDays(-1);

        var decision = await CreateSut().EvaluateAsync(created, default);

        Assert.False(decision.IsBlocked);
    }
}
