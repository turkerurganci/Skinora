namespace Skinora.Users.Application.Settings;

/// <summary>
/// Checks the caller's Steam trade hold duration (08 §2.2) and maps it to the
/// Mobile Authenticator state the frontend surfaces on the trade-URL
/// endpoint (07 §5.16a). Real implementation calls the Steam sidecar
/// <c>GetTradeHoldDurations/v1</c> (T64–T69 DI swap); until then the
/// <see cref="StubTradeHoldChecker"/> default keeps the contract live.
/// A separate abstraction from <c>IMobileAuthenticatorCheck</c>
/// (<c>Skinora.Auth</c>, T31) lets <c>Skinora.Users</c> call it without
/// adding a project reference that would invert the dependency graph.
/// </summary>
public interface ITradeHoldChecker
{
    Task<TradeHoldResult> CheckAsync(
        string steamId64,
        string tradeOfferAccessToken,
        CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="Available"/> is <c>false</c> when Steam could not be queried
/// (network failure, 5xx, or API key missing) — the caller then persists the
/// trade URL but surfaces <c>STEAM_API_UNAVAILABLE</c> (07 §5.16a fallback).
/// </summary>
public sealed record TradeHoldResult(bool Available, bool Active, string? SetupGuideUrl);
