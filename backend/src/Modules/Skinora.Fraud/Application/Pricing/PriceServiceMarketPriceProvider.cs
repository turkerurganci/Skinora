using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Pricing;

namespace Skinora.Fraud.Application.Pricing;

/// <summary>
/// WP4a — bridges the Transactions <see cref="IMarketPriceProvider"/> seam to
/// the T81 Steam Market price stack (<see cref="IPriceService"/> →
/// <c>PriceService</c> → <c>ISteamMarketPriceClient</c> + <c>ItemPriceCache</c>).
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>Skinora.Fraud</c> because that module owns <see cref="IPriceService"/>
/// and already references <c>Skinora.Transactions</c> (where the port is
/// declared); the reverse reference would be a project cycle. This mirrors the
/// <c>IAccountFlagChecker</c> / <c>AccountFlagChecker</c> placement and is
/// registered against the port by <c>TransactionsModule</c>.
/// </para>
/// <para>
/// Pure adapter — it does not touch the cache / rate-limiter / TTL logic owned
/// by <c>PriceService</c>. It preserves the seam's fail-open contract: a
/// <c>null</c> price means "no market signal", so the fraud pre-check proceeds
/// as <c>CREATED</c> rather than flagging on missing data (02 §14.4, 08 §7.4).
/// </para>
/// </remarks>
public sealed class PriceServiceMarketPriceProvider : IMarketPriceProvider
{
    private readonly IPriceService _priceService;

    public PriceServiceMarketPriceProvider(IPriceService priceService)
        => _priceService = priceService;

    public Task<decimal?> TryGetMarketPriceAsync(
        string marketHashName,
        StablecoinType denomination,
        CancellationToken cancellationToken)
    {
        // No market key → no signal (fail-open). Guard before hitting the
        // cache/HTTP stack so an empty key never becomes a wasted lookup or a
        // spurious cache-miss that masquerades as "wired but no price".
        if (string.IsNullOrWhiteSpace(marketHashName))
            return Task.FromResult<decimal?>(null);

        // denomination intentionally ignored: the T81 stack quotes a single
        // pinned currency (USD), treated 1:1 with the stablecoin (WP4a owner
        // decision). The wide price_deviation_threshold absorbs micro-variance
        // (08 §7.3). The underlying PriceService already degrades a Steam
        // outage / rate-limit to null (08 §7.4) — no exception leaks to the
        // transaction-creation hot path.
        return _priceService.GetMarketPriceAsync(marketHashName, cancellationToken);
    }
}
