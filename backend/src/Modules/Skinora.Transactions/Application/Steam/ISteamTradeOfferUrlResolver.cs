using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Steam;

/// <summary>
/// Cross-module port (WP12, T90 K3) — resolves the public Steam trade-offer URL
/// the transaction-detail endpoint surfaces in the TRADE_OFFER_SENT_TO_SELLER
/// and TRADE_OFFER_SENT_TO_BUYER states (04 §7.3 "Steam'e git linki"). The
/// Skinora.Steam module owns the <c>TradeOffer</c> entity (06 §3.9); the
/// Transactions module only references the Shared
/// <see cref="TradeOfferDirection"/> enum, keeping the dependency direction
/// Steam → Transactions (no cycle). Returns <c>null</c> when no matching offer
/// carries a <c>SteamTradeOfferId</c> yet (e.g. the dispatch row is still
/// PENDING, or the Steam module is not composed — see
/// <see cref="NullSteamTradeOfferUrlResolver"/>).
/// </summary>
public interface ISteamTradeOfferUrlResolver
{
    Task<string?> ResolveUrlAsync(
        Guid transactionId, TradeOfferDirection direction, CancellationToken cancellationToken);
}
