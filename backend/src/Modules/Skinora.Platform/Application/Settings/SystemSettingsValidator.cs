using System.Globalization;
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

        // Generic positive-number rule for everything else numeric.
        if (dataType is "int" or "decimal")
        {
            var d = decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
            if (d <= 0m)
                return $"{key} must be greater than 0 (got {value}).";
        }

        return null;
    }

    private static bool IsRatioKey(string key) => key
        is "commission_rate"
        or "gas_fee_protection_ratio"
        or "timeout_warning_ratio"
        or "price_deviation_threshold";

    // ---- Stage 3: cross-key helpers ----

    private static bool TryReadInt(IReadOnlyDictionary<string, string?> snapshot, string key, out int value)
    {
        value = 0;
        if (!snapshot.TryGetValue(key, out var raw) || raw is null) return false;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>Outcome of a <see cref="SystemSettingsValidator"/> call.</summary>
public sealed record ValidationResult(bool IsValid, string? ErrorMessage)
{
    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string reason) => new(false, reason);
}
