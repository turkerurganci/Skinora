using Skinora.Shared.Steam;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Conservative default for <see cref="ISteamAccountLimitedProbe"/> (08 §2.2a),
/// registered by the Transactions module and replaced by the sidecar-backed
/// client in <c>SteamModule</c>.
///
/// <para>
/// Reports <see cref="SteamAccountLimitedProbeResult.Unavailable"/> — "Steam
/// could not be asked" — rather than a permissive "not limited". The choice
/// mirrors <c>StubTradeHoldChecker</c> and is the whole point of this round: a
/// forgotten DI swap must not silently re-open the gate that the 2026-09-02
/// rehearsal proved was missing. The failure is loud (every gate answers a
/// retryable 503) instead of invisible.
/// </para>
///
/// <para>
/// It is deliberately NOT the "unknown, so allow" variant. That shape is the
/// fail-open this whole change removes, and a stub is exactly where it would
/// reappear unnoticed.
/// </para>
/// </summary>
public sealed class StubSteamAccountLimitedProbe : ISteamAccountLimitedProbe
{
    public Task<SteamAccountLimitedProbeResult> ProbeAsync(
        string steamId64,
        CancellationToken cancellationToken)
        => Task.FromResult(SteamAccountLimitedProbeResult.Unavailable);
}
