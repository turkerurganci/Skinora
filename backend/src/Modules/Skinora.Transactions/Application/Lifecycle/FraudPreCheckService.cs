using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Calculations;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Application.Pricing;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Default <see cref="IFraudPreCheckService"/>. Owns the SystemSetting reads,
/// transaction aggregation queries and the priority ordering between the
/// three T55 AML rules (price deviation → high volume → dormant anomaly).
/// </summary>
public sealed class FraudPreCheckService : IFraudPreCheckService
{
    public const string DeviationThresholdKey = "price_deviation_threshold";
    public const string HighVolumeAmountThresholdKey = "high_volume_amount_threshold";
    public const string HighVolumeCountThresholdKey = "high_volume_count_threshold";
    public const string HighVolumePeriodHoursKey = "high_volume_period_hours";
    public const string DormantMinAgeDaysKey = "dormant_account_min_age_days";
    public const string DormantValueThresholdKey = "dormant_account_value_threshold";

    private readonly AppDbContext _db;
    private readonly IMarketPriceProvider _marketPrice;

    public FraudPreCheckService(AppDbContext db, IMarketPriceProvider marketPrice)
    {
        _db = db;
        _marketPrice = marketPrice;
    }

    public async Task<FraudPreCheckOutcome> EvaluateAsync(
        Guid sellerId,
        string itemClassId,
        string? itemInstanceId,
        StablecoinType stablecoin,
        decimal quotedPrice,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Snapshot the market price up front — the orchestrator persists it
        // onto Transaction.MarketPriceAtCreation regardless of which rule
        // (if any) trips, so admins always see the comparison value.
        var marketPrice = await _marketPrice.TryGetMarketPriceAsync(
            itemClassId, itemInstanceId, stablecoin, cancellationToken);

        // ---------- Rule 1: PRICE_DEVIATION (most specific) ----------
        var priceDeviation = await EvaluatePriceDeviationAsync(
            quotedPrice, marketPrice, cancellationToken);
        if (priceDeviation is not null)
            return new FraudPreCheckOutcome(true, marketPrice,
                FraudFlagType.PRICE_DEVIATION, priceDeviation);

        // ---------- Rule 2: HIGH_VOLUME (rolling window) ----------
        var highVolume = await EvaluateHighVolumeAsync(
            sellerId, nowUtc, cancellationToken);
        if (highVolume is not null)
            return new FraudPreCheckOutcome(true, marketPrice,
                FraudFlagType.HIGH_VOLUME, highVolume);

        // ---------- Rule 3: ABNORMAL_BEHAVIOR (dormant anomaly) ----------
        var dormant = await EvaluateDormantAnomalyAsync(
            sellerId, quotedPrice, nowUtc, cancellationToken);
        if (dormant is not null)
            return new FraudPreCheckOutcome(true, marketPrice,
                FraudFlagType.ABNORMAL_BEHAVIOR, dormant);

        return new FraudPreCheckOutcome(false, marketPrice, null, null);
    }

    private async Task<string?> EvaluatePriceDeviationAsync(
        decimal quotedPrice,
        decimal? marketPrice,
        CancellationToken cancellationToken)
    {
        var deviation = FraudDetectionCalculator.CalculatePriceDeviation(quotedPrice, marketPrice);
        if (deviation is null) return null;

        var threshold = await ReadDecimalSettingAsync(DeviationThresholdKey, cancellationToken);
        if (threshold is null || deviation.Value <= threshold.Value) return null;

        // Shape mirrors PriceDeviationFlagDetail (07 §9.3) — InputPrice +
        // MarketPrice + DeviationPercent rounded to 4 decimals so the
        // serialized payload is deterministic across replays.
        return JsonSerializer.Serialize(new
        {
            inputPrice = quotedPrice,
            marketPrice = marketPrice ?? 0m,
            deviationPercent = Math.Round(deviation.Value * 100m, 4),
        });
    }

    private async Task<string?> EvaluateHighVolumeAsync(
        Guid sellerId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var periodHours = await ReadIntSettingAsync(HighVolumePeriodHoursKey, cancellationToken);
        if (periodHours is null or <= 0) return null;

        var countThreshold = await ReadIntSettingAsync(HighVolumeCountThresholdKey, cancellationToken);
        var amountThreshold = await ReadDecimalSettingAsync(HighVolumeAmountThresholdKey, cancellationToken);

        // Both thresholds null/zero → rule disabled, skip the DB roundtrip.
        if (countThreshold is null or <= 0 && amountThreshold is null or <= 0) return null;

        var cutoff = nowUtc - TimeSpan.FromHours(periodHours.Value);

        // Aggregate the seller's recent transactions in a single round trip.
        // Includes FLAGGED rows (the seller may have a pending pre-create
        // flag that still counts towards the rolling window) but excludes
        // soft-deleted rows. CANCELLED_* rows are kept as well — a sudden
        // burst of cancellations is itself a signal worth surfacing.
        var aggregate = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.SellerId == sellerId
                        && !t.IsDeleted
                        && t.CreatedAt >= cutoff)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(t => t.TotalAmount) })
            .FirstOrDefaultAsync(cancellationToken);

        var recentCount = aggregate?.Count ?? 0;
        var recentTotal = aggregate?.Total ?? 0m;

        if (!FraudDetectionCalculator.IsHighVolume(recentCount, recentTotal, countThreshold, amountThreshold))
            return null;

        // Shape mirrors HighVolumeFlagDetail (07 §9.3).
        return JsonSerializer.Serialize(new
        {
            periodHours = periodHours.Value,
            transactionCount = recentCount,
            totalVolume = recentTotal,
        });
    }

    private async Task<string?> EvaluateDormantAnomalyAsync(
        Guid sellerId,
        decimal quotedPrice,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var valueThreshold = await ReadDecimalSettingAsync(DormantValueThresholdKey, cancellationToken);
        if (valueThreshold is null or <= 0) return null;

        var minAgeDays = await ReadIntSettingAsync(DormantMinAgeDaysKey, cancellationToken);
        if (minAgeDays is null or < 0) return null;

        var seller = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == sellerId)
            .Select(u => new { u.CreatedAt, u.CompletedTransactionCount })
            .FirstOrDefaultAsync(cancellationToken);

        if (seller is null) return null;

        var accountAgeDays = Math.Max(0, (int)(nowUtc - seller.CreatedAt).TotalDays);

        if (!FraudDetectionCalculator.IsDormantAnomaly(
                seller.CompletedTransactionCount,
                accountAgeDays,
                quotedPrice,
                minAgeDays.Value,
                valueThreshold.Value))
            return null;

        // Shape mirrors AbnormalBehaviorFlagDetail (07 §9.3).
        return JsonSerializer.Serialize(new
        {
            pattern = "DORMANT_ACCOUNT_HIGH_VALUE",
            description = $"Hesap {accountAgeDays} gündür açık ve hiç tamamlanmış işlemi yok; "
                          + $"denenen işlem tutarı {quotedPrice} eşik {valueThreshold.Value} üzerinde.",
        });
    }

    private async Task<decimal?> ReadDecimalSettingAsync(string key, CancellationToken cancellationToken)
    {
        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == key && s.IsConfigured)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (raw is null) return null;
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private async Task<int?> ReadIntSettingAsync(string key, CancellationToken cancellationToken)
    {
        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == key && s.IsConfigured)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (raw is null) return null;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
