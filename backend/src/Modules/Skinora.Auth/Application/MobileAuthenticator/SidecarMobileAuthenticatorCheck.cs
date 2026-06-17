using Skinora.Shared.Steam;

namespace Skinora.Auth.Application.MobileAuthenticator;

/// <summary>
/// Production <see cref="IMobileAuthenticatorCheck"/> (WP6). Delegates to the
/// shared <see cref="ISteamTradeHoldProbe"/> (Skinora.Steam sidecar client →
/// 08 §2.2 <c>GetTradeHoldDurations</c>) for the A7
/// <c>POST /auth/check-authenticator</c> re-verify endpoint (07 §4.8).
/// </summary>
/// <remarks>
/// The A7 contract is binary (<c>active</c> + optional <c>setupGuideUrl</c>) —
/// it carries no "Steam unavailable" state (that distinction lives on the
/// primary U17 trade-URL path via <c>TradeHoldResult.Available</c>). So this
/// checker fails closed: an unreachable probe maps to <c>Active=false</c> with
/// the setup guide URL, identical to the genuine "MA inactive" case and to the
/// conservative <see cref="StubMobileAuthenticatorCheck"/> default — a Steam
/// outage can never silently report the authenticator as active. Replaces the
/// stub via DI swap without touching the A7 caller.
/// </remarks>
public sealed class SidecarMobileAuthenticatorCheck : IMobileAuthenticatorCheck
{
    private readonly ISteamTradeHoldProbe _probe;

    public SidecarMobileAuthenticatorCheck(ISteamTradeHoldProbe probe)
    {
        _probe = probe;
    }

    public async Task<MobileAuthenticatorResult> CheckAsync(
        string steamId64,
        string tradeOfferAccessToken,
        CancellationToken cancellationToken)
    {
        var result = await _probe.ProbeAsync(steamId64, tradeOfferAccessToken, cancellationToken);

        return result is { Available: true, MobileAuthenticatorActive: true }
            ? new MobileAuthenticatorResult(Active: true, SetupGuideUrl: null)
            : new MobileAuthenticatorResult(
                Active: false, SetupGuideUrl: StubMobileAuthenticatorCheck.DefaultSetupGuideUrl);
    }
}
