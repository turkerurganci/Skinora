using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Steam;

/// <summary>
/// Default <see cref="ISteamTradeOfferUrlResolver"/> registered by the
/// Transactions module via <c>TryAddScoped</c> so the detail service resolves
/// even when the Skinora.Steam module is not composed (Transactions-only test
/// hosts). Always returns <c>null</c> — the endpoint then omits the trade-offer
/// URL. SteamModule swaps this for the DB-backed resolver in production
/// (<c>services.Replace</c>), mirroring the
/// <c>ISteamInventoryReader</c>/<c>StubSteamInventoryReader</c> pattern.
/// </summary>
public sealed class NullSteamTradeOfferUrlResolver : ISteamTradeOfferUrlResolver
{
    public Task<string?> ResolveUrlAsync(
        Guid transactionId, TradeOfferDirection direction, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);
}
