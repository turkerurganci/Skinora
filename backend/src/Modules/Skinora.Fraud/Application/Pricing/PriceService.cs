using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Fraud.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Persistence;
using Skinora.Shared.SteamMarket;

namespace Skinora.Fraud.Application.Pricing;

/// <summary>
/// Cache-first orchestrator for Steam Market price lookups (08 §7.3).
/// Owns the <see cref="ItemPriceCache"/> table, the TTL semantics, and
/// the stale-while-revalidate background refresh enqueue.
/// </summary>
/// <remarks>
/// Behaviour matrix (08 §7.3 / 7.4):
/// <list type="table">
///   <listheader><term>Cache state</term><description>Action</description></listheader>
///   <item><term>Miss</term><description>Synchronous API fetch + upsert. API failure returns null (skip fraud check).</description></item>
///   <item><term>Fresh (≤24 h)</term><description>Return cached value. No API call.</description></item>
///   <item><term>Stale (24–48 h)</term><description>Return cached value <i>and</i> enqueue background refresh via <see cref="IBackgroundJobScheduler"/>.</description></item>
///   <item><term>Expired (>48 h)</term><description>Synchronous API fetch + upsert. On API failure, falls back to stale cache (≤48 h) before returning null.</description></item>
/// </list>
///
/// The <see cref="RefreshAsync(string)"/> method is the background-job
/// entry point — Hangfire serialises the expression
/// <c>scheduler.Enqueue&lt;PriceService&gt;(s =&gt; s.RefreshAsync(name))</c>
/// and resolves <see cref="PriceService"/> from a fresh DI scope when
/// the worker runs.
/// </remarks>
public sealed class PriceService : IPriceService
{
    private readonly AppDbContext _db;
    private readonly ISteamMarketPriceClient _client;
    private readonly IBackgroundJobScheduler _scheduler;
    private readonly SteamMarketSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<PriceService> _logger;

    public PriceService(
        AppDbContext db,
        ISteamMarketPriceClient client,
        IBackgroundJobScheduler scheduler,
        IOptions<SteamMarketSettings> settings,
        TimeProvider clock,
        ILogger<PriceService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<decimal?> GetMarketPriceAsync(string marketHashName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(marketHashName))
        {
            throw new ArgumentException("marketHashName is required.", nameof(marketHashName));
        }

        var existing = await _db.Set<ItemPriceCache>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.MarketHashName == marketHashName, cancellationToken)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow().UtcDateTime;
        var freshTtl = TimeSpan.FromHours(_settings.FreshTtlHours);
        var staleTtl = TimeSpan.FromHours(_settings.StaleTtlHours);

        if (existing is not null)
        {
            var age = now - existing.FetchedAt;

            if (age <= freshTtl)
            {
                return EffectivePrice(existing);
            }

            if (age <= staleTtl)
            {
                EnqueueRefresh(marketHashName);
                return EffectivePrice(existing);
            }
        }

        return await FetchAndUpsertAsync(marketHashName, existing, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Background-job entry point invoked by the Hangfire worker for
    /// stale cache refreshes. Swallows transport failures — the fraud
    /// pipeline must degrade gracefully per 08 §7.4 even when the
    /// refresh path keeps failing.
    /// </summary>
    public async Task RefreshAsync(string marketHashName)
    {
        if (string.IsNullOrWhiteSpace(marketHashName))
        {
            return;
        }

        try
        {
            await FetchAndUpsertAsync(marketHashName, existing: null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Background price-cache refresh failed for {MarketHashName}.",
                marketHashName);
        }
    }

    private async Task<decimal?> FetchAndUpsertAsync(
        string marketHashName,
        ItemPriceCache? existing,
        CancellationToken cancellationToken)
    {
        try
        {
            var quote = await _client.GetPriceAsync(marketHashName, cancellationToken).ConfigureAwait(false);
            await UpsertAsync(marketHashName, quote, cancellationToken).ConfigureAwait(false);
            return quote.EffectivePrice;
        }
        catch (SteamMarketException ex)
        {
            _logger.LogWarning(
                ex,
                "Steam Market API failed for {MarketHashName} — falling back to cache (≤{StaleTtl}h) or skipping fraud check.",
                marketHashName,
                _settings.StaleTtlHours);

            if (existing is not null)
            {
                var age = _clock.GetUtcNow().UtcDateTime - existing.FetchedAt;
                if (age <= TimeSpan.FromHours(_settings.StaleTtlHours))
                {
                    return EffectivePrice(existing);
                }
            }

            return null;
        }
    }

    private async Task UpsertAsync(
        string marketHashName,
        SteamMarketPriceQuote quote,
        CancellationToken cancellationToken)
    {
        var tracked = await _db.Set<ItemPriceCache>()
            .FirstOrDefaultAsync(c => c.MarketHashName == marketHashName, cancellationToken)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow().UtcDateTime;

        if (tracked is null)
        {
            _db.Set<ItemPriceCache>().Add(new ItemPriceCache
            {
                Id = Guid.NewGuid(),
                MarketHashName = marketHashName,
                MedianPrice = quote.MedianPrice,
                LowestPrice = quote.LowestPrice,
                FetchedAt = now,
                Source = ItemPriceSources.SteamMarket,
            });
        }
        else
        {
            tracked.MedianPrice = quote.MedianPrice;
            tracked.LowestPrice = quote.LowestPrice;
            tracked.FetchedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static decimal? EffectivePrice(ItemPriceCache cache) =>
        cache.MedianPrice ?? cache.LowestPrice;

    private void EnqueueRefresh(string marketHashName)
    {
        // The lambda below is an Expression<Action<PriceService>> — Hangfire
        // serialises it and the worker runs RefreshAsync in a fresh DI scope.
        // CS4014 fires because the expression body returns a Task; suppress
        // it locally rather than dropping the warning suite-wide.
#pragma warning disable CS4014
        Expression<Action<PriceService>> call = s => s.RefreshAsync(marketHashName);
#pragma warning restore CS4014

        try
        {
            _scheduler.Enqueue(call);
        }
        catch (Exception ex)
        {
            // Refresh enqueue is fire-and-forget — never bubble up; the
            // hot path already has a cached value to return.
            _logger.LogWarning(
                ex,
                "Failed to enqueue background price-cache refresh for {MarketHashName}.",
                marketHashName);
        }
    }
}
