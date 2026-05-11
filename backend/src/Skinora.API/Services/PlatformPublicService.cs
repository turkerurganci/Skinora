using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.API.Services;

/// <inheritdoc cref="IPlatformPublicService"/>
public sealed class PlatformPublicService : IPlatformPublicService
{
    /// <summary>07 §10.1 — P1 cache TTL.</summary>
    public static readonly TimeSpan StatsCacheTtl = TimeSpan.FromMinutes(15);

    /// <summary>07 §10.2 — P2 cache TTL.</summary>
    public static readonly TimeSpan MaintenanceCacheTtl = TimeSpan.FromSeconds(30);

    public const string StatsCacheKey = "platform:stats";
    public const string MaintenanceCacheKey = "platform:maintenance";

    /// <summary>
    /// Sentinel persisted in <see cref="SystemSetting.Value"/> for inactive
    /// optional string fields. Mirrors <c>auth.banned_countries</c> /
    /// <c>multi_account.exchange_addresses</c> conventions (06 §3.17).
    /// </summary>
    private const string NoneSentinel = "NONE";

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly PlatformOptions _options;

    public PlatformPublicService(
        AppDbContext db,
        IMemoryCache cache,
        IOptions<PlatformOptions> options)
    {
        _db = db;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<PlatformStatsResponse> GetStatsAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<PlatformStatsResponse>(StatsCacheKey, out var cached) && cached is not null)
            return cached;

        var totalCompleted = await _db.Set<Transaction>()
            .AsNoTracking()
            .CountAsync(t => t.Status == TransactionStatus.COMPLETED, cancellationToken);

        var response = new PlatformStatsResponse(
            TotalCompletedTransactions: totalCompleted,
            PlatformUptimePercent: _options.UptimePercent);

        _cache.Set(StatsCacheKey, response, StatsCacheTtl);
        return response;
    }

    public async Task<PlatformMaintenanceResponse> GetMaintenanceAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<PlatformMaintenanceResponse>(MaintenanceCacheKey, out var cached) && cached is not null)
            return cached;

        // Only the four maintenance keys — small, indexed read.
        var keys = new[]
        {
            "platform.maintenance.active",
            "platform.maintenance.type",
            "platform.maintenance.message",
            "platform.maintenance.planned_end",
        };

        var rows = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => keys.Contains(s.Key))
            .Select(s => new { s.Key, s.Value })
            .ToListAsync(cancellationToken);

        var byKey = rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);

        var active = byKey.TryGetValue("platform.maintenance.active", out var activeRaw) &&
                     bool.TryParse(activeRaw, out var parsed) && parsed;

        var response = new PlatformMaintenanceResponse(
            Active: active,
            Type: NormaliseSentinel(Read(byKey, "platform.maintenance.type")),
            Message: NormaliseSentinel(Read(byKey, "platform.maintenance.message")),
            PlannedEnd: NormaliseSentinel(Read(byKey, "platform.maintenance.planned_end")));

        _cache.Set(MaintenanceCacheKey, response, MaintenanceCacheTtl);
        return response;
    }

    private static string? Read(Dictionary<string, string?> map, string key) =>
        map.TryGetValue(key, out var raw) ? raw : null;

    private static string? NormaliseSentinel(string? value) =>
        string.Equals(value, NoneSentinel, StringComparison.Ordinal) ? null : value;
}
