using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Settings;

public sealed class SteamTradeUrlService : ISteamTradeUrlService
{
    private readonly AppDbContext _db;
    private readonly ITradeUrlParser _parser;

    public SteamTradeUrlService(AppDbContext db, ITradeUrlParser parser)
    {
        _db = db;
        _parser = parser;
    }

    public async Task<TradeUrlUpdateResult> UpdateAsync(
        Guid userId,
        string? tradeUrl,
        TradeUrlMaOutcome maOutcome,
        CancellationToken cancellationToken)
    {
        var parsed = _parser.Parse(tradeUrl);
        if (parsed is null)
            return TradeUrlUpdateResult.Failure(TradeUrlUpdateStatus.InvalidTradeUrl);

        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(
                u => u.Id == userId && !u.IsDeactivated, cancellationToken);

        if (user is null)
            return TradeUrlUpdateResult.Failure(TradeUrlUpdateStatus.UserNotFound);

        // Always persist the parsed URL + tokens so the frontend can redisplay
        // the saved value. The MA flag follows Steam's answer, or falls back
        // to the prior value if the API was unavailable — 07 §5.16a:
        // "Steam API erişilemezse: Trade URL kaydedilir ama MA doğrulaması
        // pending state'e alınır."
        user.SteamTradeUrl = parsed.Normalized;
        user.SteamTradePartner = parsed.Partner;
        user.SteamTradeAccessToken = parsed.Token;

        if (maOutcome.ApiAvailable)
        {
            user.MobileAuthenticatorVerified = maOutcome.Active;
        }
        else
        {
            // Pending state — keep any prior MA=true (don't regress) but force
            // false on first-ever save so the user can't start a transaction
            // until Steam responds.
            if (user.MobileAuthenticatorVerified == false)
            {
                user.MobileAuthenticatorVerified = false;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (!maOutcome.ApiAvailable)
            return TradeUrlUpdateResult.Pending(parsed.Normalized);

        return TradeUrlUpdateResult.Success(
            parsed.Normalized, maOutcome.Active, maOutcome.Active ? null : maOutcome.SetupGuideUrl);
    }
}
