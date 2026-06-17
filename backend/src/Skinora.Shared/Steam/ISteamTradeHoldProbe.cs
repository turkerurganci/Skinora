namespace Skinora.Shared.Steam;

/// <summary>
/// Read port over the Steam trade-hold (escrow) duration check — 08 §2.2.
/// Steam exposes no direct "Mobile Authenticator active?" endpoint, so the
/// platform infers it from <c>IEconService/GetTradeHoldDurations/v1</c>: a
/// target escrow hold of 0 seconds means the user runs the Mobile
/// Authenticator. This is a Web-API-key call (no bot session) keyed on the
/// platform API key + target SteamID64 + <c>trade_offer_access_token</c>.
///
/// The interface lives in <c>Skinora.Shared</c> because two sibling modules
/// consume it without a cross-reference:
/// <list type="bullet">
///   <item><description><c>SidecarTradeHoldChecker</c> (Skinora.Users → <c>ITradeHoldChecker</c>) — U17 trade-URL save.</description></item>
///   <item><description><c>SidecarMobileAuthenticatorCheck</c> (Skinora.Auth → <c>IMobileAuthenticatorCheck</c>) — A7 re-verify.</description></item>
/// </list>
/// The concrete impl (<c>HttpSteamTradeHoldClient</c>) lives in Skinora.Steam
/// next to the sidecar HTTP plumbing, mirroring the <c>ISteamInventoryReader</c>
/// arrangement.
/// </summary>
public interface ISteamTradeHoldProbe
{
    /// <summary>
    /// Probe the trade-hold duration for <paramref name="steamId64"/> using the
    /// <paramref name="tradeOfferAccessToken"/> parsed from the user's trade URL
    /// (mandatory for non-friend targets — 08 §2.2). Never throws: transport /
    /// upstream / configuration failures resolve to
    /// <see cref="SteamTradeHoldProbeResult.Available"/> = <c>false</c> so callers
    /// fail closed onto the 07 §5.16a <c>STEAM_API_UNAVAILABLE</c> fallback.
    /// </summary>
    Task<SteamTradeHoldProbeResult> ProbeAsync(
        string steamId64,
        string tradeOfferAccessToken,
        CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of a trade-hold probe.
/// </summary>
/// <param name="Available">
/// <c>true</c> only when Steam was queried successfully. <c>false</c> when the
/// sidecar/Steam could not be reached or the API key is missing — the caller
/// then surfaces <c>STEAM_API_UNAVAILABLE</c> and blocks transaction start.
/// </param>
/// <param name="MobileAuthenticatorActive">
/// <c>true</c> when the target escrow hold is 0 seconds. Only meaningful when
/// <paramref name="Available"/> is <c>true</c>.
/// </param>
public sealed record SteamTradeHoldProbeResult(bool Available, bool MobileAuthenticatorActive)
{
    /// <summary>Successful probe where the Mobile Authenticator is active (no hold).</summary>
    public static readonly SteamTradeHoldProbeResult Active = new(Available: true, MobileAuthenticatorActive: true);

    /// <summary>Successful probe where the Mobile Authenticator is inactive (escrow hold &gt; 0).</summary>
    public static readonly SteamTradeHoldProbeResult Inactive = new(Available: true, MobileAuthenticatorActive: false);

    /// <summary>Steam could not be queried — caller falls back to STEAM_API_UNAVAILABLE.</summary>
    public static readonly SteamTradeHoldProbeResult Unavailable = new(Available: false, MobileAuthenticatorActive: false);
}
