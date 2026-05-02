using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// SystemSetting-backed implementation of <see cref="ITransactionLimitsProvider"/>.
/// Direct AsNoTracking query, no caching layer (mirrors T43 reader pattern).
/// Bundles the entire <see cref="TransactionLimits"/> record into a single
/// round-trip so the eligibility + creation paths each read once.
/// </summary>
public sealed class TransactionLimitsProvider : ITransactionLimitsProvider
{
    public const string MaxConcurrentKey = "max_concurrent_transactions";
    public const string NewAccountLimitKey = "new_account_transaction_limit";
    public const string NewAccountPeriodKey = "new_account_period_days";
    public const string PayoutCooldownKey = "wallet.payout_address_cooldown_hours";
    public const string AcceptTimeoutKey = "accept_timeout_minutes";
    public const string PaymentTimeoutMinKey = "payment_timeout_min_minutes";
    public const string PaymentTimeoutMaxKey = "payment_timeout_max_minutes";
    public const string PaymentTimeoutDefaultKey = "payment_timeout_default_minutes";
    public const string CommissionRateKey = "commission_rate";
    public const string MinAmountKey = "min_transaction_amount";
    public const string MaxAmountKey = "max_transaction_amount";
    public const string OpenLinkEnabledKey = "open_link_enabled";

    private static readonly string[] _allKeys =
    [
        MaxConcurrentKey, NewAccountLimitKey, NewAccountPeriodKey, PayoutCooldownKey,
        AcceptTimeoutKey,
        PaymentTimeoutMinKey, PaymentTimeoutMaxKey, PaymentTimeoutDefaultKey,
        CommissionRateKey, MinAmountKey, MaxAmountKey, OpenLinkEnabledKey,
    ];

    private readonly AppDbContext _db;

    public TransactionLimitsProvider(AppDbContext db)
    {
        _db = db;
    }

    public async Task<TransactionLimits> GetAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => _allKeys.Contains(s.Key) && s.IsConfigured)
            .Select(s => new { s.Key, s.Value })
            .ToDictionaryAsync(r => r.Key, r => r.Value, cancellationToken);

        return new TransactionLimits(
            MaxConcurrent: ReadOptionalPositiveInt(rows, MaxConcurrentKey),
            NewAccountTransactionLimit: ReadOptionalPositiveInt(rows, NewAccountLimitKey),
            NewAccountPeriodDays: ReadOptionalPositiveInt(rows, NewAccountPeriodKey),
            PayoutAddressCooldownHours: ReadOptionalPositiveInt(rows, PayoutCooldownKey),
            AcceptTimeoutMinutes: ReadOptionalPositiveInt(rows, AcceptTimeoutKey),
            PaymentTimeoutMinMinutes: ReadOptionalPositiveInt(rows, PaymentTimeoutMinKey),
            PaymentTimeoutMaxMinutes: ReadOptionalPositiveInt(rows, PaymentTimeoutMaxKey),
            PaymentTimeoutDefaultMinutes: ReadOptionalPositiveInt(rows, PaymentTimeoutDefaultKey),
            CommissionRate: ReadOptionalPositiveDecimal(rows, CommissionRateKey),
            MinTransactionAmount: ReadOptionalPositiveDecimal(rows, MinAmountKey),
            MaxTransactionAmount: ReadOptionalPositiveDecimal(rows, MaxAmountKey),
            OpenLinkEnabled: ReadBool(rows, OpenLinkEnabledKey, fallback: false));
    }

    private static int? ReadOptionalPositiveInt(IReadOnlyDictionary<string, string?> rows, string key)
    {
        if (!rows.TryGetValue(key, out var raw) || raw is null) return null;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            return parsed;
        return null;
    }

    private static decimal? ReadOptionalPositiveDecimal(IReadOnlyDictionary<string, string?> rows, string key)
    {
        if (!rows.TryGetValue(key, out var raw) || raw is null) return null;
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            return parsed;
        return null;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, string?> rows, string key, bool fallback)
    {
        if (!rows.TryGetValue(key, out var raw) || raw is null) return fallback;
        if (bool.TryParse(raw, out var parsed)) return parsed;
        return fallback;
    }
}
