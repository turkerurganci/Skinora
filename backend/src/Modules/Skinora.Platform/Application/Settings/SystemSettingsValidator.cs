using System.Globalization;
using Cronos;
using Skinora.Platform.Domain.Entities;

namespace Skinora.Platform.Application.Settings;

/// <summary>
/// Two-stage validator for SystemSetting writes (06 §3.17 — type check + range
/// + cross-key). Used by both the admin update path and the startup
/// bootstrap, so a value rejected by an admin can never be re-introduced via
/// env var hydration.
/// </summary>
/// <remarks>
/// <para>Stage 1 — Type: parses the raw string against the row's
/// <c>DataType</c> (int / decimal / bool / string).</para>
/// <para>Stage 2 — Range: per-key min/max/format rules (06 §3.17, e.g.
/// <c>commission_rate</c> must satisfy <c>0 &lt; x &lt; 1</c>).</para>
/// <para>Stage 3 — Cross-key: invariants spanning multiple settings
/// (<c>payment_timeout_min &lt; payment_timeout_max</c>, monitoring polling
/// 24h &lt; 7d &lt; 30d). Cross-key is only invoked once *all* peer keys are
/// configured; admins can stage a single key in isolation without tripping a
/// false-positive.</para>
/// </remarks>
public sealed class SystemSettingsValidator
{
    public static SystemSettingsValidator Instance { get; } = new();

    /// <summary>
    /// Hard cap for string SystemSetting values — matches the
    /// <c>SystemSetting.Value</c> column width (<c>nvarchar(500)</c>,
    /// <c>SystemSettingConfiguration</c>). Enforced here so an over-long value is
    /// rejected with a clean VALIDATION_ERROR (400) on both the admin update path
    /// (AD9) and the maintenance toggle (AD30) instead of a DB truncation error
    /// (500) on SaveChanges.
    /// </summary>
    public const int MaxStringValueLength = 500;

    /// <summary>
    /// Permitted values for <c>platform.maintenance.type</c> (T63a / 07 §10.2).
    /// <c>NONE</c> is the inactive sentinel — the public endpoint emits it as
    /// JSON <c>null</c>. Cross-key check rejects <c>type=NONE</c> while
    /// <c>active=true</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> MaintenanceTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "PLANNED_MAINTENANCE",
        "PLATFORM_MAINTENANCE",
        "STEAM_OUTAGE",
        "BLOCKCHAIN_DEGRADATION",
        "NONE",
    };

    /// <summary>
    /// Floor for <c>payout_settlement_days</c> (T129 — 02 §4.5.1, §16.2). Steam
    /// keeps a traded item reversible for 7 days and either side can start the
    /// reversal, so a settlement window shorter than that pays the seller while
    /// they can still take the item back. The seeded default is 8 (7 + one day
    /// of margin); this is the hard floor an admin cannot go under.
    /// </summary>
    public const int MinimumSettlementDays = 7;

    /// <summary>
    /// Validate a single key/value tuple in isolation (type + range only).
    /// Caller is responsible for invoking <see cref="ValidateCrossKey"/>
    /// against the post-write SystemSettings snapshot.
    /// </summary>
    public ValidationResult ValidateSingle(string key, string? value, string dataType)
    {
        if (value is null)
            return ValidationResult.Fail("Value is required.");

        if (!TryValidateType(value, dataType, out var typeReason))
            return ValidationResult.Fail(typeReason);

        var rangeReason = ValidateRange(key, value, dataType);
        if (rangeReason is not null)
            return ValidationResult.Fail(rangeReason);

        return ValidationResult.Ok();
    }

    /// <summary>
    /// Cross-key invariants — invoked with the full post-write snapshot.
    /// Returns the first violation found or <see cref="ValidationResult.Ok"/>.
    /// </summary>
    public ValidationResult ValidateCrossKey(IReadOnlyDictionary<string, string?> snapshot)
    {
        // payment_timeout_min < payment_timeout_max (06 §3.17)
        if (TryReadInt(snapshot, "payment_timeout_min_minutes", out var pmin) &&
            TryReadInt(snapshot, "payment_timeout_max_minutes", out var pmax) &&
            pmin >= pmax)
        {
            return ValidationResult.Fail(
                "payment_timeout_min_minutes must be strictly less than payment_timeout_max_minutes.");
        }

        // payment_timeout_default within [min, max]
        if (TryReadInt(snapshot, "payment_timeout_min_minutes", out var dmin) &&
            TryReadInt(snapshot, "payment_timeout_max_minutes", out var dmax) &&
            TryReadInt(snapshot, "payment_timeout_default_minutes", out var pdef) &&
            (pdef < dmin || pdef > dmax))
        {
            return ValidationResult.Fail(
                "payment_timeout_default_minutes must be within [payment_timeout_min_minutes, payment_timeout_max_minutes].");
        }

        // Monitoring 24h < 7d < 30d (06 §3.17 — logical order)
        if (TryReadInt(snapshot, "monitoring_post_cancel_24h_polling_seconds", out var p24) &&
            TryReadInt(snapshot, "monitoring_post_cancel_7d_polling_seconds", out var p7) &&
            p24 >= p7)
        {
            return ValidationResult.Fail(
                "monitoring_post_cancel_24h_polling_seconds must be strictly less than monitoring_post_cancel_7d_polling_seconds.");
        }
        if (TryReadInt(snapshot, "monitoring_post_cancel_7d_polling_seconds", out var pp7) &&
            TryReadInt(snapshot, "monitoring_post_cancel_30d_polling_seconds", out var p30) &&
            pp7 >= p30)
        {
            return ValidationResult.Fail(
                "monitoring_post_cancel_7d_polling_seconds must be strictly less than monitoring_post_cancel_30d_polling_seconds.");
        }

        // T63a — active maintenance must declare a concrete type (07 §10.2:
        // C08 banner styling depends on the type discriminator).
        if (TryReadBool(snapshot, "platform.maintenance.active", out var maintActive) &&
            maintActive &&
            snapshot.TryGetValue("platform.maintenance.type", out var maintType) &&
            string.Equals(maintType, "NONE", StringComparison.Ordinal))
        {
            return ValidationResult.Fail(
                "platform.maintenance.type must be set (not 'NONE') when platform.maintenance.active is true.");
        }

        return ValidationResult.Ok();
    }

    /// <summary>
    /// Convenience over <see cref="ValidateCrossKey"/> for callers that hold
    /// the full row set — extracts a key→value snapshot first.
    /// </summary>
    public ValidationResult ValidateCrossKey(IEnumerable<SystemSetting> rows)
    {
        var snapshot = rows
            .Where(r => r.IsConfigured)
            .ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
        return ValidateCrossKey(snapshot);
    }

    // ---- Stage 1: type ----

    private static bool TryValidateType(string value, string dataType, out string reason)
    {
        reason = string.Empty;
        switch (dataType)
        {
            case "int":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    reason = $"'{value}' is not an integer.";
                    return false;
                }
                return true;

            case "decimal":
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                {
                    reason = $"'{value}' is not a decimal.";
                    return false;
                }
                return true;

            case "bool":
                if (!bool.TryParse(value, out _))
                {
                    reason = $"'{value}' must be 'true' or 'false'.";
                    return false;
                }
                return true;

            case "string":
                if (string.IsNullOrEmpty(value))
                {
                    reason = "string value cannot be empty.";
                    return false;
                }
                if (value.Length > MaxStringValueLength)
                {
                    reason = $"string value exceeds the {MaxStringValueLength}-character limit (got {value.Length}).";
                    return false;
                }
                return true;

            default:
                reason = $"unknown DataType '{dataType}'.";
                return false;
        }
    }

    // ---- Stage 2: per-key range ----

    private static string? ValidateRange(string key, string value, string dataType)
    {
        // Ratio keys — strictly between 0 and 1 (open interval, 06 §3.17).
        if (IsRatioKey(key))
        {
            var d = decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
            if (d <= 0m || d >= 1m)
                return $"{key} must satisfy 0 < value < 1 (got {value}).";
            return null;
        }

        // min_refund_threshold_ratio is a multiplier that legitimately exceeds 1
        // (default 2.0 — iade < gas fee × 2.0 ise iade yapılmaz). The generic
        // positive-number rule below would also accept it, but the explicit
        // branch documents the intent and keeps the rule discoverable.
        if (key == "min_refund_threshold_ratio")
        {
            var d = decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
            if (d <= 0m)
                return $"{key} must be greater than 0 (got {value}).";
            return null;
        }

        // WP4a — price_deviation_threshold is a deviation ratio that
        // legitimately exceeds 1: |quoted - market| / market can be > 100%
        // (08 §7.3 worked example is 282%), and the spec recommends a WIDE
        // threshold (≥ 100%, i.e. ≥ 1.0) to absorb Steam single-source
        // variance. It is therefore NOT an open-(0,1) ratio key — only the
        // positive floor applies (mirrors min_refund_threshold_ratio above).
        if (key == "price_deviation_threshold")
        {
            var d = decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
            if (d <= 0m)
                return $"{key} must be greater than 0 (got {value}).";
            return null;
        }

        // T129 — the settlement window must cover Steam's reversal window
        // (02 §16.2: "Steam'in geri alma penceresinden kısa ayarlanmamalıdır").
        // The floor is the whole point of the setting: a window shorter than 7
        // days pays the seller while they can still reverse the trade, which is
        // the exact fraud 02 §4.5.1 exists to close. The generic positive-number
        // rule would happily accept 1.
        if (key == "payout_settlement_days")
        {
            var d = int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (d < MinimumSettlementDays)
            {
                return $"{key} must be at least {MinimumSettlementDays} days — it has to cover "
                    + $"Steam's 7-day trade reversal window (02 §4.5.1, §16.2) (got {value}).";
            }
            return null;
        }

        // T63a — platform.maintenance.type must be one of the documented enum
        // values or the "NONE" sentinel (07 §10.2).
        if (key == "platform.maintenance.type")
        {
            if (!MaintenanceTypes.Contains(value))
                return $"{key} must be one of {string.Join(", ", MaintenanceTypes.OrderBy(v => v))} (got '{value}').";
            return null;
        }

        // T63a — platform.maintenance.planned_end is an ISO-8601 UTC timestamp
        // or the "NONE" sentinel.
        if (key == "platform.maintenance.planned_end")
        {
            if (string.Equals(value, "NONE", StringComparison.Ordinal))
                return null;
            if (!DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out _))
            {
                return $"{key} must be ISO-8601 (e.g. '2026-03-16T18:00:00Z') or 'NONE' (got '{value}').";
            }
            return null;
        }

        // Country CSV — uppercase ISO-3166-1 alpha-2 entries or the literal "NONE".
        if (key == "auth.banned_countries")
        {
            var trimmed = value.Trim();
            if (string.Equals(trimmed, "NONE", StringComparison.OrdinalIgnoreCase))
                return null;
            var parts = trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return "auth.banned_countries must contain at least one ISO-3166-1 alpha-2 code or be 'NONE'.";
            foreach (var p in parts)
            {
                if (p.Length != 2 || !p.All(char.IsLetter))
                    return $"auth.banned_countries entry '{p}' must be a 2-letter ISO-3166-1 alpha-2 code.";
            }
            return null;
        }

        // WP14 — cron-schedule keys must parse as a standard 5-field (or
        // 6-field with seconds) cron expression. Rejecting a typo here returns
        // a clean 400 on BOTH the admin update path (AD9) and the env-var
        // bootstrap, instead of silently failing later at Hangfire
        // RecurringJob.AddOrUpdate (which would log a warning and leave the job
        // on its previous schedule). The CronSettingChangePropagator re-registers
        // the job after a successful write, so a value that passes here must be
        // one Hangfire will accept.
        if (IsCronKey(key))
        {
            if (!TryValidateCron(value, out var cronReason))
                return $"{key} is not a valid cron expression ({cronReason}).";
            return null;
        }

        // Generic positive-number rule for everything else numeric.
        if (dataType is "int" or "decimal")
        {
            var d = decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
            if (d <= 0m)
                return $"{key} must be greater than 0 (got {value}).";
        }

        return null;
    }

    // WP14 — settings whose value is a cron expression consumed by a Hangfire
    // recurring-job registrar (ReconciliationJobRegistrar, HotWalletMonitorJobRegistrar).
    private static bool IsCronKey(string key) => key
        is "reconciliation.schedule_cron"
        or "hot_wallet.monitor_cron";

    /// <summary>
    /// Parse <paramref name="value"/> as a cron expression, accepting both the
    /// standard 5-field form and the 6-field (leading seconds) form that
    /// Hangfire also supports. Returns <c>true</c> when either format parses.
    /// </summary>
    private static bool TryValidateCron(string value, out string reason)
    {
        var trimmed = value.Trim();
        reason = string.Empty;
        foreach (var format in new[] { CronFormat.Standard, CronFormat.IncludeSeconds })
        {
            try
            {
                CronExpression.Parse(trimmed, format);
                return true;
            }
            catch (CronFormatException ex)
            {
                reason = ex.Message;
            }
        }

        return false;
    }

    // NOTE: price_deviation_threshold is intentionally NOT here — it is a
    // deviation ratio that legitimately exceeds 1 (see the explicit >0 branch
    // in ValidateRange). The open-(0,1) keys below are genuine fractions.
    private static bool IsRatioKey(string key) => key
        is "commission_rate"
        or "gas_fee_protection_ratio"
        or "timeout_warning_ratio";

    // ---- Stage 3: cross-key helpers ----

    private static bool TryReadInt(IReadOnlyDictionary<string, string?> snapshot, string key, out int value)
    {
        value = 0;
        if (!snapshot.TryGetValue(key, out var raw) || raw is null) return false;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadBool(IReadOnlyDictionary<string, string?> snapshot, string key, out bool value)
    {
        value = false;
        if (!snapshot.TryGetValue(key, out var raw) || raw is null) return false;
        return bool.TryParse(raw, out value);
    }
}

/// <summary>Outcome of a <see cref="SystemSettingsValidator"/> call.</summary>
public sealed record ValidationResult(bool IsValid, string? ErrorMessage)
{
    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string reason) => new(false, reason);
}
