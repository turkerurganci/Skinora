namespace Skinora.Users.Application.Settings;

/// <summary>
/// Conservative default: always reports the authenticator as inactive with
/// the public Steam setup URL, and <see cref="TradeHoldResult.Available"/>
/// true (so the caller does not misinterpret as "Steam down"). Chosen over
/// a permissive "active=true" stub so a forgotten DI swap cannot silently
/// let non-authenticator accounts past the transaction-start gate.
/// Swapped for a real sidecar-backed impl at T64–T69.
/// </summary>
public sealed class StubTradeHoldChecker : ITradeHoldChecker
{
    public const string DefaultSetupGuideUrl =
        "https://help.steampowered.com/en/faqs/view/06B0-26E6-2CF8-C3C7";

    public Task<TradeHoldResult> CheckAsync(
        string steamId64,
        string tradeOfferAccessToken,
        CancellationToken cancellationToken)
        => Task.FromResult(new TradeHoldResult(
            Available: true, Active: false, SetupGuideUrl: DefaultSetupGuideUrl));
}
