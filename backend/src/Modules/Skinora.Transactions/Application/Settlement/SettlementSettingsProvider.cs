using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;

namespace Skinora.Transactions.Application.Settlement;

/// <summary>
/// SystemSetting-backed live reader for the settlement window (T129 — 02
/// §4.5.1). Mirrors <c>GasFeeSettingsProvider</c> / <c>TransactionLimitsProvider</c>:
/// direct <c>AsNoTracking</c> dictionary fetch, no caching, documented defaults
/// when a row is missing or malformed.
/// </summary>
/// <remarks>
/// Every fallback here is the SAFE direction, not merely the seeded one. A
/// poisoned <c>payout_settlement_days</c> row must never shorten the window
/// below Steam's reversal period (that is the whole protection), and a poisoned
/// gate row must never read as "auto-refund enabled" — so the day count is
/// floored at <see cref="SystemSettingsValidatorFloorDays"/> on the read side
/// too, and anything other than a parsable <c>true</c> keeps the gate closed.
/// </remarks>
public sealed class SettlementSettingsProvider : ISettlementSettingsProvider
{
    public const string SettlementDaysKey = "payout_settlement_days";
    public const string UnreadableEscalationHoursKey = "settlement.unreadable_escalation_hours";
    public const string ReversalAutoRefundEnabledKey = "settlement.reversal_auto_refund_enabled";

    /// <summary>Seeded default — 7-day Steam reversal window + 1 day margin.</summary>
    public const int DefaultSettlementDays = 8;

    /// <summary>Seeded default for the unreadable-inventory escalation.</summary>
    public const int DefaultUnreadableEscalationHours = 48;

    /// <summary>
    /// Read-side floor, mirroring <c>SystemSettingsValidator.MinimumSettlementDays</c>.
    /// Duplicated as a literal because Skinora.Transactions does not reference
    /// the Platform settings application layer (same reason
    /// <c>SweepQueueJob.HotWalletAddressKey</c> is duplicated).
    /// </summary>
    public const int SystemSettingsValidatorFloorDays = 7;

    private static readonly string[] _allKeys =
    [
        SettlementDaysKey,
        UnreadableEscalationHoursKey,
        ReversalAutoRefundEnabledKey,
    ];

    private readonly AppDbContext _db;

    public SettlementSettingsProvider(AppDbContext db)
    {
        _db = db;
    }

    public async Task<SettlementSettings> GetAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => _allKeys.Contains(s.Key) && s.IsConfigured)
            .Select(s => new { s.Key, s.Value })
            .ToDictionaryAsync(r => r.Key, r => r.Value, cancellationToken);

        return new SettlementSettings(
            SettlementDays: ReadSettlementDays(rows),
            UnreadableEscalationHours: ReadUnreadableEscalationHours(rows),
            ReversalAutoRefundEnabled: ReadReversalAutoRefundEnabled(rows));
    }

    private static int ReadSettlementDays(IReadOnlyDictionary<string, string?> rows)
    {
        if (rows.TryGetValue(SettlementDaysKey, out var raw)
            && raw is not null
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= SystemSettingsValidatorFloorDays)
        {
            return parsed;
        }
        return DefaultSettlementDays;
    }

    private static int ReadUnreadableEscalationHours(IReadOnlyDictionary<string, string?> rows)
    {
        if (rows.TryGetValue(UnreadableEscalationHoursKey, out var raw)
            && raw is not null
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0)
        {
            return parsed;
        }
        return DefaultUnreadableEscalationHours;
    }

    private static bool ReadReversalAutoRefundEnabled(IReadOnlyDictionary<string, string?> rows)
    {
        // Fail-closed, exactly like DeliveryVerificationService's launch gate: a
        // wrongly closed gate delays a decision until a human looks, a wrongly
        // open one refunds a buyer and fraud-flags a seller on an inference no
        // real reversal has ever been measured against (T122 runbook §7).
        return rows.TryGetValue(ReversalAutoRefundEnabledKey, out var raw)
            && bool.TryParse(raw, out var enabled)
            && enabled;
    }
}
