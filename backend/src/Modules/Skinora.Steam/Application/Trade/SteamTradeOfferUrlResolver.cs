using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Application.Steam;

namespace Skinora.Steam.Application.Trade;

/// <summary>
/// DB-backed <see cref="ISteamTradeOfferUrlResolver"/> (WP12, T90 K3). Resolves
/// the public Steam trade-offer URL for the most recently sent offer of the
/// requested direction that already carries a <c>SteamTradeOfferId</c>. Lives
/// in Skinora.Steam because it owns the <see cref="TradeOffer"/> entity
/// (06 §3.9); SteamModule swaps it in for the Transactions-side null default.
/// </summary>
public sealed class SteamTradeOfferUrlResolver : ISteamTradeOfferUrlResolver
{
    // 04 §7.3 — the "Steam'e git" CTA opens the canonical Steam trade-offer
    // page. Steam ignores the optional ?partner/token query for viewing, so the
    // bare /tradeoffer/{id}/ form is sufficient for the user to inspect/accept.
    private const string TradeOfferUrlTemplate = "https://steamcommunity.com/tradeoffer/{0}/";

    private readonly AppDbContext _db;

    public SteamTradeOfferUrlResolver(AppDbContext db) => _db = db;

    public async Task<string?> ResolveUrlAsync(
        Guid transactionId, TradeOfferDirection direction, CancellationToken cancellationToken)
    {
        // A transaction can carry multiple offers per direction across retries;
        // the latest sent one with a real Steam id is the actionable one.
        var offerId = await _db.Set<TradeOffer>()
            .AsNoTracking()
            .Where(o => o.TransactionId == transactionId
                        && o.Direction == direction
                        && o.SteamTradeOfferId != null)
            .OrderByDescending(o => o.SentAt)
            .Select(o => o.SteamTradeOfferId)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrEmpty(offerId)
            ? null
            : string.Format(CultureInfo.InvariantCulture, TradeOfferUrlTemplate, offerId);
    }
}
