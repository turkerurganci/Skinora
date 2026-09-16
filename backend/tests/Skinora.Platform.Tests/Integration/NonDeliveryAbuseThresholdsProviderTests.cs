using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Platform.Infrastructure.Reputation;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Platform.Tests.Integration;

/// <summary>
/// 02 §14.2 — <see cref="NonDeliveryAbuseThresholdsProvider"/> read against the
/// REAL seed. Every evaluator test stubs the thresholds, so without this a key
/// spelled differently here and in <see cref="SystemSettingSeed"/> would read
/// as "unconfigured", switch the rule off in production and leave every test
/// green.
/// </summary>
public class NonDeliveryAbuseThresholdsProviderTests : IntegrationTestBase
{
    static NonDeliveryAbuseThresholdsProviderTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Reads_The_Seeded_Owner_Defaults_And_Is_Enabled()
    {
        var thresholds = await new NonDeliveryAbuseThresholdsProvider(Context).GetAsync(CancellationToken.None);

        Assert.Equal(30, thresholds.WindowDays);
        Assert.Equal(2, thresholds.FlagCount);
        Assert.Equal(3, thresholds.SuspendCount);
        Assert.True(thresholds.IsEnabled);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Reads_An_Admin_Change()
    {
        await SetAsync(NonDeliveryAbuseThresholdsProvider.SuspendCountKey, "5");

        var thresholds = await new NonDeliveryAbuseThresholdsProvider(Context).GetAsync(CancellationToken.None);

        Assert.Equal(5, thresholds.SuspendCount);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task An_Unusable_Value_Disables_The_Rule(string? value)
    {
        // Disabled, not defaulted: a half-configured environment must not start
        // suspending sellers on a number nobody chose.
        await SetAsync(NonDeliveryAbuseThresholdsProvider.FlagCountKey, value);

        var thresholds = await new NonDeliveryAbuseThresholdsProvider(Context).GetAsync(CancellationToken.None);

        Assert.Equal(0, thresholds.FlagCount);
        Assert.False(thresholds.IsEnabled);
    }

    private async Task SetAsync(string key, string? value)
    {
        var row = await Context.Set<SystemSetting>().SingleAsync(s => s.Key == key);
        row.Value = value;
        row.IsConfigured = value is not null;
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }
}
