using Skinora.Platform.Application.Settings;

namespace Skinora.Platform.Tests.Unit.Settings;

/// <summary>
/// Unit coverage for <see cref="SystemSettingsValidator"/> — the type/range/
/// cross-key pipeline used by both the admin update API and the startup
/// bootstrap (T41). Tests intentionally avoid EF — the validator is
/// stateless and side-effect free.
/// </summary>
public class SystemSettingsValidatorTests
{
    private readonly SystemSettingsValidator _v = SystemSettingsValidator.Instance;

    // ---- Type ----

    [Theory]
    [InlineData("int", "42", true)]
    // Negative ints fail the per-key generic positive rule (max_concurrent_transactions
    // > 0) — kept here to document that ValidateSingle runs type *and* range together.
    [InlineData("int", "-3", false)]
    [InlineData("int", "abc", false)]
    [InlineData("int", "1.5", false)]
    [InlineData("decimal", "0.5", true)]
    [InlineData("decimal", "10", true)]
    [InlineData("decimal", "x", false)]
    [InlineData("bool", "true", true)]
    [InlineData("bool", "FALSE", true)]
    [InlineData("bool", "yes", false)]
    [InlineData("string", "NONE", true)]
    [InlineData("string", "", false)]
    public void ValidateSingle_TypeStage(string dataType, string value, bool expected)
    {
        // Use a key whose range rule is permissive for the value (any positive int/decimal).
        var key = dataType switch
        {
            "bool" => "open_link_enabled",
            "string" => "auth.banned_countries",
            _ => "max_concurrent_transactions",
        };
        var result = _v.ValidateSingle(key, value, dataType);
        Assert.Equal(expected, result.IsValid);
    }

    [Fact]
    public void ValidateSingle_NullValue_Fails()
    {
        var result = _v.ValidateSingle("commission_rate", null, "decimal");
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(500, true)]   // exactly the nvarchar(500) column cap — allowed
    [InlineData(501, false)]  // one over — rejected before it reaches the DB
    public void ValidateSingle_String_EnforcesColumnLengthCap(int length, bool expected)
    {
        // platform.maintenance.message has no per-key range rule, so only the
        // type-stage string rules apply (non-empty + the 500-char column cap).
        var value = new string('x', length);
        var result = _v.ValidateSingle("platform.maintenance.message", value, "string");
        Assert.Equal(expected, result.IsValid);
    }

    // ---- Range — ratio keys (0 < x < 1) ----

    [Theory]
    [InlineData("commission_rate", "0.02", true)]
    [InlineData("commission_rate", "0.5", true)]
    [InlineData("commission_rate", "0", false)]
    [InlineData("commission_rate", "1", false)]
    [InlineData("commission_rate", "1.5", false)]
    [InlineData("timeout_warning_ratio", "0.75", true)]
    [InlineData("timeout_warning_ratio", "1.0", false)]
    [InlineData("gas_fee_protection_ratio", "0.10", true)]
    [InlineData("gas_fee_protection_ratio", "1", false)]
    public void ValidateSingle_RatioKeys_Enforce_OpenZeroOne(string key, string value, bool expected)
    {
        var result = _v.ValidateSingle(key, value, "decimal");
        Assert.Equal(expected, result.IsValid);
    }

    // ---- Range — refund threshold may exceed 1 ----

    [Theory]
    [InlineData("2.0", true)]
    [InlineData("5.5", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    public void ValidateSingle_MinRefundThresholdRatio_AllowsAboveOne(string value, bool expected)
    {
        var result = _v.ValidateSingle("min_refund_threshold_ratio", value, "decimal");
        Assert.Equal(expected, result.IsValid);
    }

    // ---- Range — price deviation threshold may exceed 1 (WP4a) ----
    // deviation = |quoted-market|/market can be >100% (08 §7.3 = 282%), and the
    // spec recommends a WIDE threshold (≥100% = ≥1.0). It is a positive ratio,
    // NOT an open-(0,1) fraction — 1.0 and 1.5 must validate.

    [Theory]
    [InlineData("0.25", true)]
    [InlineData("1.0", true)]
    [InlineData("1.5", true)]
    [InlineData("2.82", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    public void ValidateSingle_PriceDeviationThreshold_AllowsAboveOne(string value, bool expected)
    {
        var result = _v.ValidateSingle("price_deviation_threshold", value, "decimal");
        Assert.Equal(expected, result.IsValid);
    }

    // ---- Range — generic positive int/decimal ----

    [Theory]
    [InlineData("accept_timeout_minutes", "60", true)]
    [InlineData("accept_timeout_minutes", "0", false)]
    [InlineData("accept_timeout_minutes", "-5", false)]
    [InlineData("max_transaction_amount", "10000.0", true)]
    [InlineData("max_transaction_amount", "0", false)]
    public void ValidateSingle_GenericPositiveRule(string key, string value, bool expected)
    {
        var dt = key.Contains("amount") || key.Contains("ratio") ? "decimal" : "int";
        var result = _v.ValidateSingle(key, value, dt);
        Assert.Equal(expected, result.IsValid);
    }

    // ---- Range — banned_countries CSV ----

    [Theory]
    [InlineData("NONE", true)]
    [InlineData("none", true)]
    [InlineData("IR", true)]
    [InlineData("IR,KP,CU", true)]
    [InlineData("ir,kp", true)]
    [InlineData("USA", false)]
    [InlineData("I1", false)]
    [InlineData("", false)]
    [InlineData(",", false)]
    public void ValidateSingle_BannedCountries_AcceptsIso2OrNone(string value, bool expected)
    {
        var result = _v.ValidateSingle("auth.banned_countries", value, "string");
        Assert.Equal(expected, result.IsValid);
    }

    // ---- Cross-key — payment timeout ordering ----

    [Fact]
    public void ValidateCrossKey_PaymentTimeout_MinMustBeLessThanMax()
    {
        var snapshot = new Dictionary<string, string?>
        {
            ["payment_timeout_min_minutes"] = "30",
            ["payment_timeout_max_minutes"] = "30",
            ["payment_timeout_default_minutes"] = "30",
        };
        var result = _v.ValidateCrossKey(snapshot);
        Assert.False(result.IsValid);
        Assert.Contains("payment_timeout_min_minutes", result.ErrorMessage);
    }

    [Fact]
    public void ValidateCrossKey_PaymentTimeout_DefaultOutsideBounds_Fails()
    {
        var snapshot = new Dictionary<string, string?>
        {
            ["payment_timeout_min_minutes"] = "15",
            ["payment_timeout_max_minutes"] = "60",
            ["payment_timeout_default_minutes"] = "120",
        };
        var result = _v.ValidateCrossKey(snapshot);
        Assert.False(result.IsValid);
        Assert.Contains("payment_timeout_default_minutes", result.ErrorMessage);
    }

    [Fact]
    public void ValidateCrossKey_Monitoring_24h_Lt_7d_Lt_30d()
    {
        var snapshot = new Dictionary<string, string?>
        {
            ["monitoring_post_cancel_24h_polling_seconds"] = "300",
            ["monitoring_post_cancel_7d_polling_seconds"] = "30",
            ["monitoring_post_cancel_30d_polling_seconds"] = "3600",
        };
        var result = _v.ValidateCrossKey(snapshot);
        Assert.False(result.IsValid);
        Assert.Contains("monitoring_post_cancel_24h", result.ErrorMessage);
    }

    [Fact]
    public void ValidateCrossKey_AllValidValues_Passes()
    {
        var snapshot = new Dictionary<string, string?>
        {
            ["payment_timeout_min_minutes"] = "15",
            ["payment_timeout_max_minutes"] = "60",
            ["payment_timeout_default_minutes"] = "30",
            ["monitoring_post_cancel_24h_polling_seconds"] = "30",
            ["monitoring_post_cancel_7d_polling_seconds"] = "300",
            ["monitoring_post_cancel_30d_polling_seconds"] = "3600",
        };
        var result = _v.ValidateCrossKey(snapshot);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateCrossKey_PartialSnapshot_DoesNotTrigger_FalsePositive()
    {
        // Only payment_timeout_min set — without max, the rule should pass
        // (cross-key rules require both peers).
        var snapshot = new Dictionary<string, string?>
        {
            ["payment_timeout_min_minutes"] = "999",
        };
        var result = _v.ValidateCrossKey(snapshot);
        Assert.True(result.IsValid);
    }

    // ---- Range — T63a maintenance type enum ----

    [Theory]
    [InlineData("PLANNED_MAINTENANCE", true)]
    [InlineData("PLATFORM_MAINTENANCE", true)]
    [InlineData("STEAM_OUTAGE", true)]
    [InlineData("BLOCKCHAIN_DEGRADATION", true)]
    [InlineData("NONE", true)]
    [InlineData("planned_maintenance", false)] // case-sensitive
    [InlineData("HARDWARE_FAILURE", false)]
    [InlineData("", false)]
    public void ValidateSingle_MaintenanceType_AcceptsKnownEnum(string value, bool expected)
    {
        var result = _v.ValidateSingle("platform.maintenance.type", value, "string");
        Assert.Equal(expected, result.IsValid);
    }

    // ---- Range — T63a maintenance planned_end ISO 8601 / NONE ----

    [Theory]
    [InlineData("NONE", true)]
    [InlineData("2026-03-16T18:00:00Z", true)]
    [InlineData("2026-03-16T18:00:00+00:00", true)]
    [InlineData("2026-03-16T18:00:00", true)] // assumed-universal parser accepts naked
    [InlineData("not-a-date", false)]
    [InlineData("16/03/2026", false)] // dd/MM/yyyy not ISO
    public void ValidateSingle_MaintenancePlannedEnd_RequiresIsoOrNone(string value, bool expected)
    {
        var result = _v.ValidateSingle("platform.maintenance.planned_end", value, "string");
        Assert.Equal(expected, result.IsValid);
    }

    // ---- Range — WP14 cron-schedule keys ----
    // reconciliation.schedule_cron / hot_wallet.monitor_cron must parse as a
    // standard 5-field (or 6-field with seconds) cron so a typo is rejected with
    // a 400 here instead of silently failing later at Hangfire registration.

    [Theory]
    [InlineData("reconciliation.schedule_cron", "0 3 * * *", true)]    // daily 03:00 — seed default
    [InlineData("hot_wallet.monitor_cron", "*/15 * * * *", true)]      // every 15 min — seed default
    [InlineData("reconciliation.schedule_cron", "0 0 * * 0", true)]    // weekly (Sunday)
    [InlineData("hot_wallet.monitor_cron", "0 */15 * * * *", true)]    // 6-field (leading seconds)
    [InlineData("reconciliation.schedule_cron", "not a cron", false)]  // gibberish
    [InlineData("hot_wallet.monitor_cron", "99 99 * * *", false)]      // out-of-range fields
    [InlineData("reconciliation.schedule_cron", "* * *", false)]       // too few fields
    public void ValidateSingle_CronKeys_RejectMalformedExpressions(string key, string value, bool expected)
    {
        var result = _v.ValidateSingle(key, value, "string");
        Assert.Equal(expected, result.IsValid);
        if (!expected)
            Assert.Contains("cron", result.ErrorMessage!);
    }

    // ---- Cross-key — T63a active=true ⇒ type≠NONE ----

    [Fact]
    public void ValidateCrossKey_MaintenanceActive_RequiresType()
    {
        var snapshot = new Dictionary<string, string?>
        {
            ["platform.maintenance.active"] = "true",
            ["platform.maintenance.type"] = "NONE",
        };
        var result = _v.ValidateCrossKey(snapshot);
        Assert.False(result.IsValid);
        Assert.Contains("platform.maintenance.type", result.ErrorMessage);
    }

    [Fact]
    public void ValidateCrossKey_MaintenanceActive_WithType_Passes()
    {
        var snapshot = new Dictionary<string, string?>
        {
            ["platform.maintenance.active"] = "true",
            ["platform.maintenance.type"] = "PLATFORM_MAINTENANCE",
        };
        var result = _v.ValidateCrossKey(snapshot);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateCrossKey_MaintenanceInactive_TypeNoneIsAllowed()
    {
        var snapshot = new Dictionary<string, string?>
        {
            ["platform.maintenance.active"] = "false",
            ["platform.maintenance.type"] = "NONE",
        };
        var result = _v.ValidateCrossKey(snapshot);
        Assert.True(result.IsValid);
    }
}
