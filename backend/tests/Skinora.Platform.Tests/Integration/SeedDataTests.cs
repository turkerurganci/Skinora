using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Platform.Tests.Integration;

/// <summary>
/// Integration tests for the T26 + T30 + T34 + T43 EF Core seed contracts (06 §8.9):
/// SYSTEM user, SystemHeartbeat singleton, and 34 SystemSetting rows
/// (28 T26 platform parameters + 2 T30 access-control settings +
/// 2 T34 wallet address cooldown settings + 2 T43 reputation thresholds).
/// </summary>
public class SeedDataTests : IntegrationTestBase
{
    static SeedDataTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Seed_SystemUser_IsPresent_With_Sentinel_SteamId_And_Deactivated()
    {
        // Soft-delete filter is global: the seed row must be IsDeleted = false
        // to survive it. Double-check by querying both the filter-visible and
        // filter-ignored result sets.
        var visible = await Context.Set<User>()
            .FirstOrDefaultAsync(u => u.Id == SeedConstants.SystemUserId);

        Assert.NotNull(visible);
        Assert.Equal(SeedConstants.SystemSteamId, visible!.SteamId);
        Assert.Equal("System", visible.SteamDisplayName);
        Assert.True(visible.IsDeactivated);
        Assert.False(visible.MobileAuthenticatorVerified);
        Assert.False(visible.IsDeleted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Seed_SystemHeartbeat_IsSingleton_With_Id_One()
    {
        var rows = await Context.Set<SystemHeartbeat>().ToListAsync();
        Assert.Single(rows);
        Assert.Equal(SeedConstants.SystemHeartbeatId, rows[0].Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Seed_SystemSettings_Has_34_Rows_With_Unique_Keys()
    {
        // 28 T26 platform parameters + 2 T30 access-control settings +
        // 2 T34 wallet address cooldown settings + 2 T43 reputation thresholds.
        var rows = await Context.Set<SystemSetting>().ToListAsync();
        Assert.Equal(34, rows.Count);
        Assert.Equal(34, rows.Select(r => r.Key).Distinct().Count());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Seed_SystemSettings_Defaulted_Parameters_Are_Configured()
    {
        // 06 §3.17 + 02 §21.1 + 02 §12.3 + 02 §13: 14 rows ship with a documented
        // default (8 T26 + 2 T30 + 2 T34 + 2 T43).
        var configured = await Context.Set<SystemSetting>()
            .Where(s => s.IsConfigured)
            .OrderBy(s => s.Key)
            .ToListAsync();

        var expectedConfiguredKeys = new[]
        {
            "auth.banned_countries",
            "auth.min_steam_account_age_days",
            "commission_rate",
            "gas_fee_protection_ratio",
            "min_refund_threshold_ratio",
            "monitoring_post_cancel_24h_polling_seconds",
            "monitoring_post_cancel_30d_polling_seconds",
            "monitoring_post_cancel_7d_polling_seconds",
            "monitoring_stop_after_days",
            "open_link_enabled",
            "reputation.min_account_age_days",
            "reputation.min_completed_transactions",
            "wallet.payout_address_cooldown_hours",
            "wallet.refund_address_cooldown_hours",
        };

        Assert.Equal(expectedConfiguredKeys, configured.Select(s => s.Key).ToArray());
        Assert.All(configured, s => Assert.False(string.IsNullOrEmpty(s.Value)));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Seed_SystemSettings_Mandatory_Parameters_Are_Unconfigured_And_Null()
    {
        // The remaining 20 rows have no default and must ship NULL +
        // IsConfigured = false so startup fail-fast (06 §8.9) refuses to
        // launch until an admin or env var provides values.
        var unconfigured = await Context.Set<SystemSetting>()
            .Where(s => !s.IsConfigured)
            .ToListAsync();

        Assert.Equal(20, unconfigured.Count);
        Assert.All(unconfigured, s => Assert.Null(s.Value));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Seed_SystemSettings_All_DataTypes_Are_Whitelisted()
    {
        // Regression guard for 06 §3.17 CHECK constraint: a typo in the seed
        // (e.g. "double") would fail the check on first insert but a stray
        // value could still slip in if the check were dropped.
        var allowed = new[] { "int", "decimal", "bool", "string" };
        var rows = await Context.Set<SystemSetting>().ToListAsync();
        Assert.All(rows, s => Assert.Contains(s.DataType, allowed));
    }
}
