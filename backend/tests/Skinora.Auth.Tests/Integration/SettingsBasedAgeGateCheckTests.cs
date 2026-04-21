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
        // The T30 migration seeds auth.min_steam_account_age_days = 30 on DB
        // creation. Tests update the existing row rather than insert (unique
        // Key constraint would reject a duplicate insert).
        var existing = await Context.Set<SystemSetting>()
            .SingleOrDefaultAsync(s => s.Key == SettingsBasedAgeGateCheck.SettingKey);

        if (existing is null)
        {
            Context.Set<SystemSetting>().Add(new SystemSetting
            {
                Id = Guid.NewGuid(),
                Key = SettingsBasedAgeGateCheck.SettingKey,
                Value = days.ToString(),
                IsConfigured = true,
                DataType = "int",
                Category = "AccessControl",
                Description = "test",
            });
        }
        else
        {
            existing.Value = days.ToString();
            existing.IsConfigured = true;
        }

        await Context.SaveChangesAsync();
    }

    private async Task ClearThresholdAsync()
    {
        var existing = await Context.Set<SystemSetting>()
            .SingleOrDefaultAsync(s => s.Key == SettingsBasedAgeGateCheck.SettingKey);
        if (existing is null) return;

        // Mark unconfigured so the check's IsConfigured predicate filters it out.
        existing.Value = null;
        existing.IsConfigured = false;
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
        // Setting exists but is marked IsConfigured = false → the check's
        // predicate filters it out and thresholdDays falls back to 0.
        await ClearThresholdAsync();
        var created = _clock.GetUtcNow().UtcDateTime.AddDays(-1);

        var decision = await CreateSut().EvaluateAsync(created, default);

        Assert.False(decision.IsBlocked);
    }
}
