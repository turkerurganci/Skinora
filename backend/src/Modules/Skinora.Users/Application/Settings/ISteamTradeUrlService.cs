namespace Skinora.Users.Application.Settings;

/// <summary>
/// Persists a parsed trade URL + the outcome of the Mobile Authenticator
/// check (07 §5.16a, 08 §2.2). The MA check itself lives in <c>Skinora.Auth</c>
/// (<c>IMobileAuthenticatorCheck</c>) — the controller invokes it and passes
/// the result here rather than forcing <c>Skinora.Users</c> to reference
/// <c>Skinora.Auth</c> (the project-reference graph points the other way).
/// </summary>
public interface ISteamTradeUrlService
{
    Task<TradeUrlUpdateResult> UpdateAsync(
        Guid userId,
        string? tradeUrl,
        TradeUrlMaOutcome maOutcome,
        CancellationToken cancellationToken);
}

public enum TradeUrlUpdateStatus
{
    Success,
    UserNotFound,
    InvalidTradeUrl,
    SteamApiUnavailable,
}

/// <summary>
/// Outcome of the Mobile Authenticator trade-hold check, supplied by the
/// controller. <see cref="ApiAvailable"/> lets the service distinguish the
/// "active=false" branch (persist + warn) from the "couldn't ask Steam"
/// branch (persist as pending + surface 503 — 07 §5.16a Steam API
/// erişilemezse).
/// </summary>
public sealed record TradeUrlMaOutcome(bool ApiAvailable, bool Active, string? SetupGuideUrl);

public sealed record TradeUrlUpdateResult(
    TradeUrlUpdateStatus Status,
    string? TradeUrl,
    bool MobileAuthenticatorActive,
    string? SetupGuideUrl)
{
    public static TradeUrlUpdateResult Success(
        string tradeUrl, bool maActive, string? setupGuideUrl)
        => new(TradeUrlUpdateStatus.Success, tradeUrl, maActive, setupGuideUrl);

    public static TradeUrlUpdateResult Failure(TradeUrlUpdateStatus status)
        => new(status, null, false, null);

    public static TradeUrlUpdateResult Pending(string tradeUrl)
        => new(TradeUrlUpdateStatus.SteamApiUnavailable, tradeUrl, false, null);
}
