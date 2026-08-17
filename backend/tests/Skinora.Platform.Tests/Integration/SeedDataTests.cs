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
/// Integration tests for the T26 + T30 + T34 + T43 + T55 + T56 + T63a + T63b + T72 + T73 + T74 + T76 EF Core seed contracts (06 §8.9):
/// SYSTEM user, SystemHeartbeat singleton, and 56 SystemSetting rows
/// (28 T26 platform parameters + 2 T30 access-control settings +
/// 2 T34 wallet address cooldown settings + 2 T43 reputation thresholds +
/// 2 T55 dormant-account fraud thresholds +
/// 1 T56 multi-account exchange address allowlist +
/// 4 T63a platform.maintenance.{active,type,message,planned_end} settings +
/// 8 T63b retention.{outbox,processed_event,external_idempotency,orphan_notification,user_login_log}_days
/// and retention.batch_size_{outbox,notification,user_login_log} settings +
/// 1 T72 blockchain.refund_gas_fee_estimate_usdt setting +
/// 1 T73 blockchain.transfer_retry_intervals_minutes setting +
/// 2 T74 blockchain.sweep_{energy_delegation,trx_fallback}_sun settings +
/// 3 T76 reconciliation.{schedule_cron,hot_wallet_address,cold_wallet_address} settings).
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
    public async Task Seed_SystemSettings_Has_56_Rows_With_Unique_Keys()
    {
        // 28 T26 platform parameters + 2 T30 access-control settings +
        // 2 T34 wallet address cooldown settings + 2 T43 reputation thresholds +
        // 2 T55 dormant-account fraud thresholds +
        // 1 T56 multi-account exchange address allowlist +
        // 4 T63a platform.maintenance.{active,type,message,planned_end} settings +
        // 8 T63b retention.* settings (5 age windows + 3 batch sizes) +
        // 1 T72 blockchain.refund_gas_fee_estimate_usdt setting +
        // 1 T73 blockchain.transfer_retry_intervals_minutes setting +
        // 2 T74 blockchain.sweep_{energy_delegation,trx_fallback}_sun settings +
        // 3 T76 reconciliation.{schedule_cron,hot_wallet_address,cold_wallet_address} settings +
        // 2 T77 hot_wallet.{monitor_cron,trx_balance_minimum} settings +
        // 1 WP1 blockchain.payout_gas_fee_estimate_usdt setting +
        // 1 T125 delivery.inventory_evidence_auto_release_enabled launch gate +
        // 3 T129 settlement settings (payout_settlement_days,
        //   settlement.unreadable_escalation_hours,
        //   settlement.reversal_auto_refund_enabled).
        var rows = await Context.Set<SystemSetting>().ToListAsync();
        Assert.Equal(63, rows.Count);
        Assert.Equal(63, rows.Select(r => r.Key).Distinct().Count());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Seed_SystemSettings_Defaulted_Parameters_Are_Configured()
    {
        // 06 §3.17 + 02 §21.1 + 02 §12.3 + 02 §13 + 02 §14.3 + 07 §10.2 + T63b retention + T72 refund estimate + T73 retry intervals + T74 sweep amounts + T76 reconciliation cron + NONE-sentinel hot/cold addresses + T77 hot wallet monitor + TRX floor:
        // 44 rows ship with a documented default (8 T26 + 2 T30 + 2 T34 + 2 T43 + 1 T55
        // + 1 T56 + 4 T63a + 8 T63b + 1 T72 + 1 T73 + 2 T74 + 3 T76 + 2 T77 + 1 WP1
        // + 1 WP4a price_deviation_threshold=1.0
        // + 1 WP12 timeout_warning_ratio=0.75
        // + 1 T125 delivery.inventory_evidence_auto_release_enabled=false — the
        //   launch gate ships CONFIGURED on purpose so SettingsBootstrapService
        //   can never hydrate it from an env var (06 §8.9): opening it is a
        //   human decision made after reading captured evidence, not a deploy
        //   variable (DEPLOY_RUNBOOK §H)
        // + 3 T129 settlement rows (window, escalation threshold, and the
        //   auto-refund launch gate — the gate ships configured for the same
        //   reason the T125 one does, DEPLOY_RUNBOOK §I).
        // T76 hot/cold wallet addresses follow the auth.banned_countries
        // NONE-sentinel pattern: shipped configured with "NONE", treated as
        // skipped scope by ReconciliationService until production deploy
        // overrides them.
        var configured = await Context.Set<SystemSetting>()
            .Where(s => s.IsConfigured)
            .OrderBy(s => s.Key)
            .ToListAsync();

        var expectedConfiguredKeys = new[]
        {
            "auth.banned_countries",
            "auth.min_steam_account_age_days",
            "blockchain.payout_gas_fee_estimate_usdt",
            "blockchain.refund_gas_fee_estimate_usdt",
            "blockchain.sweep_energy_delegation_sun",
            "blockchain.sweep_trx_fallback_sun",
            "blockchain.transfer_retry_intervals_minutes",
            "commission_rate",
            "delivery.inventory_evidence_auto_release_enabled",
            "dormant_account_min_age_days",
            "gas_fee_protection_ratio",
            "hot_wallet.monitor_cron",
            "hot_wallet.trx_balance_minimum",
            "min_refund_threshold_ratio",
            "monitoring_post_cancel_24h_polling_seconds",
            "monitoring_post_cancel_30d_polling_seconds",
            "monitoring_post_cancel_7d_polling_seconds",
            "monitoring_stop_after_days",
            "multi_account.exchange_addresses",
            "open_link_enabled",
            // T129 — the settlement window ships with its documented default
            // (8 days = Steam's 7-day reversal window + one day of margin), so
            // a deploy that configures nothing is still protected.
            "payout_settlement_days",
            "platform.maintenance.active",
            "platform.maintenance.message",
            "platform.maintenance.planned_end",
            "platform.maintenance.type",
            "price_deviation_threshold",
            "reconciliation.cold_wallet_address",
            "reconciliation.hot_wallet_address",
            "reconciliation.schedule_cron",
            "reputation.min_account_age_days",
            "reputation.min_completed_transactions",
            "retention.batch_size_notification",
            "retention.batch_size_outbox",
            "retention.batch_size_user_login_log",
            "retention.external_idempotency_days",
            "retention.orphan_notification_days",
            "retention.outbox_message_days",
            "retention.processed_event_days",
            "retention.user_login_log_days",
            // T129 — both ship CONFIGURED for the same reason the T125 gate
            // does: the auto-refund switch must not be flippable from a deploy
            // variable (06 §8.9), and the escalation threshold has a documented
            // default that needs no operator decision.
            "settlement.reversal_auto_refund_enabled",
            "settlement.unreadable_escalation_hours",
            "timeout_warning_ratio",
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
        // The remaining 19 rows have no default and must ship NULL +
        // IsConfigured = false so startup fail-fast (06 §8.9) refuses to
        // launch until an admin or env var provides values. WP4a flipped
        // price_deviation_threshold (21→20) and WP12 flipped
        // timeout_warning_ratio (20→19) from Unconfigured to a seeded default,
        // so neither is deploy-mandatory anymore.
        // T76 reconciliation hot/cold wallet addresses follow the NONE-sentinel
        // pattern (Default("NONE")) instead of Unconfigured because their env
        // var key includes a dot which the env var provider cannot bind safely.
        var unconfigured = await Context.Set<SystemSetting>()
            .Where(s => !s.IsConfigured)
            .ToListAsync();

        Assert.Equal(19, unconfigured.Count);
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
