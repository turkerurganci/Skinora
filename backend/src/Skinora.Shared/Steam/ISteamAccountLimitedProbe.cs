namespace Skinora.Shared.Steam;

/// <summary>
/// Read port over the Steam "limited account" check — 08 §2.2a.
///
/// Steam blocks accounts that have never spent US$5 from trading at all. This
/// is a THIRD condition, independent of both the escrow hold and the account
/// age, and it is the one the 2026-09-02 live rehearsal hit: the buyer's
/// payment was already confirmed on-chain before the item turned out to be
/// undeliverable (<c>Docs/TEST_REPORTS/REHEARSAL_2026-09-02.md</c>).
///
/// <para>
/// Why this is NOT folded into <see cref="ISteamTradeHoldProbe"/>: a limited
/// account also reports <c>escrow_end_duration_seconds = 0</c>, so the
/// trade-hold probe answers "MA active" for an account that can never trade.
/// Collapsing the two would rebuild the exact inference that caused the defect.
/// The two also run on DIFFERENT Steam rate budgets — Web API (60/min) versus
/// Steam Community (10/min) — and only this one is cached.
/// </para>
///
/// The interface lives in <c>Skinora.Shared</c> for the same reason as its
/// sibling: two modules consume it (<c>Skinora.Transactions</c> gates and
/// <c>Skinora.Users</c>) without a cross-project reference. The concrete impl
/// (<c>HttpSteamAccountLimitedClient</c>) lives in Skinora.Steam next to the
/// rest of the sidecar HTTP plumbing.
/// </summary>
public interface ISteamAccountLimitedProbe
{
    /// <summary>
    /// Probe whether <paramref name="steamId64"/> is limited. No trade token is
    /// needed — the underlying Steam Community profile document is anonymous,
    /// which is what makes this check usable on the seller-side gate that holds
    /// no token for the account it is about to clear.
    ///
    /// Never throws: transport / upstream / parse / configuration failures
    /// resolve to <see cref="SteamAccountLimitedProbeResult.Unavailable"/> so
    /// callers fail closed. A missing field is a failure, NOT a "not limited" —
    /// the upstream returns HTTP 200 for both of its error shapes, so
    /// <c>IsSuccessStatusCode</c> proves nothing here.
    /// </summary>
    Task<SteamAccountLimitedProbeResult> ProbeAsync(
        string steamId64,
        CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of a limited-account probe.
/// </summary>
/// <param name="Available">
/// <c>true</c> only when Steam was queried successfully and the flag was read.
/// <c>false</c> when the sidecar could not be reached or the profile document
/// did not carry the field — the caller then surfaces a TRANSIENT error and
/// blocks, never a permanent "this account is limited".
/// </param>
/// <param name="IsLimited">
/// <c>true</c> when Steam reports the account as limited (cannot trade). Only
/// meaningful when <paramref name="Available"/> is <c>true</c>.
/// </param>
public sealed record SteamAccountLimitedProbeResult(bool Available, bool IsLimited)
{
    /// <summary>Successful probe: the account is NOT limited and may trade.</summary>
    public static readonly SteamAccountLimitedProbeResult Clear = new(Available: true, IsLimited: false);

    /// <summary>Successful probe: Steam reports the account as limited.</summary>
    public static readonly SteamAccountLimitedProbeResult Limited = new(Available: true, IsLimited: true);

    /// <summary>Steam could not be queried — caller fails closed on a transient code.</summary>
    public static readonly SteamAccountLimitedProbeResult Unavailable = new(Available: false, IsLimited: false);
}
