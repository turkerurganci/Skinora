using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Pricing;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Default <see cref="IFraudPreCheckService"/>. Reads the
/// <c>price_deviation_threshold</c> SystemSetting (02 §14.4) and asks the
/// injected <see cref="IMarketPriceProvider"/> for a comparable market price
/// — when both are present and the deviation exceeds the threshold the
/// caller is told to persist the transaction as <c>FLAGGED</c>.
/// </summary>
public sealed class FraudPreCheckService : IFraudPreCheckService
{
    public const string DeviationThresholdKey = "price_deviation_threshold";

    private readonly AppDbContext _db;
    private readonly IMarketPriceProvider _marketPrice;

    public FraudPreCheckService(AppDbContext db, IMarketPriceProvider marketPrice)
    {
        _db = db;
        _marketPrice = marketPrice;
    }

    public async Task<FraudPreCheckOutcome> EvaluateAsync(
        string itemClassId,
        string? itemInstanceId,
        StablecoinType stablecoin,
        decimal quotedPrice,
        CancellationToken cancellationToken)
    {
        var marketPrice = await _marketPrice.TryGetMarketPriceAsync(
            itemClassId, itemInstanceId, stablecoin, cancellationToken);
        var threshold = await ReadThresholdAsync(cancellationToken);

        if (marketPrice is null or <= 0)
            return new FraudPreCheckOutcome(false, marketPrice, null, threshold);

        var deviation = Math.Abs(quotedPrice - marketPrice.Value) / marketPrice.Value;

        if (threshold is null)
            return new FraudPreCheckOutcome(false, marketPrice, deviation, null);

        var shouldFlag = deviation > threshold.Value;
        return new FraudPreCheckOutcome(shouldFlag, marketPrice, deviation, threshold);
    }

    private async Task<decimal?> ReadThresholdAsync(CancellationToken cancellationToken)
    {
        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == DeviationThresholdKey && s.IsConfigured)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (raw is null) return null;
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            return parsed;
        return null;
    }
}
