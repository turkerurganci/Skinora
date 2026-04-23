namespace Skinora.Auth.Application.MobileAuthenticator;

/// <summary>
/// Abstraction for Steam Mobile Authenticator verification — 07 §4.8, 08 §2.2.
/// Real production implementation calls the Steam sidecar, which in turn
/// invokes <c>IEconService/GetTradeHoldDurations/v1</c>; hold duration of
/// <c>0</c> means the authenticator is active. The sidecar endpoint is
/// delivered in the Steam Sidecar task block (T64–T69); until then
/// <see cref="StubMobileAuthenticatorCheck"/> keeps the API contract live
/// and is replaced by DI swap without touching callers.
/// </summary>
public interface IMobileAuthenticatorCheck
{
    Task<MobileAuthenticatorResult> CheckAsync(
        string steamId64,
        string tradeOfferAccessToken,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of a Mobile Authenticator check. When <see cref="Active"/> is
/// <c>false</c>, <see cref="SetupGuideUrl"/> points to Steam's public setup
/// guide so the frontend can deep-link the user.
/// </summary>
public sealed record MobileAuthenticatorResult(bool Active, string? SetupGuideUrl);
