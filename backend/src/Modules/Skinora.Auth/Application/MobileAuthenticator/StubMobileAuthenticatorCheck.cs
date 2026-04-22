namespace Skinora.Auth.Application.MobileAuthenticator;

/// <summary>
/// Conservative default — reports the authenticator as inactive and hands
/// back Steam's official setup URL. Chosen over a permissive "active=true"
/// stub so a forgotten DI wiring cannot silently let non-authenticator
/// accounts through the wallet-change gate. Real impl arrives with the
/// Steam sidecar tasks (T64–T69).
/// </summary>
public sealed class StubMobileAuthenticatorCheck : IMobileAuthenticatorCheck
{
    public const string DefaultSetupGuideUrl =
        "https://help.steampowered.com/en/faqs/view/06B0-26E6-2CF8-C3C7";

    public Task<MobileAuthenticatorResult> CheckAsync(
        string steamId64,
        string tradeOfferAccessToken,
        CancellationToken cancellationToken)
        => Task.FromResult(new MobileAuthenticatorResult(false, DefaultSetupGuideUrl));
}
