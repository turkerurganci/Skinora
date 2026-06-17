using Skinora.Shared.Steam;

namespace Skinora.Users.Application.Settings;

/// <summary>
/// Production <see cref="ITradeHoldChecker"/> (WP6). Delegates to the shared
/// <see cref="ISteamTradeHoldProbe"/> (Skinora.Steam sidecar client → 08 §2.2
/// <c>GetTradeHoldDurations</c>) and maps the probe outcome onto the 07 §5.16a
/// trade-URL response:
/// <list type="bullet">
///   <item><description>Steam unreachable → <c>Available=false</c> (caller surfaces STEAM_API_UNAVAILABLE, blocks transaction start).</description></item>
///   <item><description>MA active (0 hold) → <c>Active=true</c>, no setup URL.</description></item>
///   <item><description>MA inactive (escrow hold) → <c>Active=false</c> + the public setup guide URL so the FE can deep-link the user.</description></item>
/// </list>
/// Replaces <see cref="StubTradeHoldChecker"/> via DI swap (SteamModule) without
/// touching the U17 caller. The probe never throws, so this stays allocation-
/// thin and exception-free.
/// </summary>
public sealed class SidecarTradeHoldChecker : ITradeHoldChecker
{
    private readonly ISteamTradeHoldProbe _probe;

    public SidecarTradeHoldChecker(ISteamTradeHoldProbe probe)
    {
        _probe = probe;
    }

    public async Task<TradeHoldResult> CheckAsync(
        string steamId64,
        string tradeOfferAccessToken,
        CancellationToken cancellationToken)
    {
        var result = await _probe.ProbeAsync(steamId64, tradeOfferAccessToken, cancellationToken);

        if (!result.Available)
            return new TradeHoldResult(Available: false, Active: false, SetupGuideUrl: null);

        return result.MobileAuthenticatorActive
            ? new TradeHoldResult(Available: true, Active: true, SetupGuideUrl: null)
            : new TradeHoldResult(
                Available: true, Active: false, SetupGuideUrl: StubTradeHoldChecker.DefaultSetupGuideUrl);
    }
}
