using Microsoft.Extensions.Logging;
using Skinora.Shared.Steam;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// 08 §2.2a — "may this Steam account trade at all?", answered from the two
/// conditions the escrow probe cannot see.
///
/// <para>
/// Trade eligibility is THREE independent conditions and this type deliberately
/// does not collapse them into one boolean:
/// <list type="number">
///   <item><description>the account is not <b>limited</b> (Steam blocks accounts with no US$5 lifetime spend);</description></item>
///   <item><description>the account has cleared Steam's <b>15-day</b> trade wait;</description></item>
///   <item><description>the escrow hold is 0 seconds (Mobile Authenticator) — answered elsewhere by <c>ITradeHoldChecker</c>.</description></item>
/// </list>
/// Before this round only (3) was read anywhere, and (3) returns "fine" for an
/// account failing (1) or (2): the 2026-09-02 rehearsal escrowed a buyer's
/// payment on-chain and only then discovered the item could never be delivered
/// (🔴 <c>Prova-LimitedAccountNeverChecked</c>).
/// </para>
/// </summary>
public interface ISteamTradeEligibilityChecker
{
    /// <summary>
    /// Evaluate <paramref name="user"/>. Never throws; an unreadable answer
    /// comes back as <see cref="SteamTradeEligibilityStatus.Unknown"/> and the
    /// caller fails closed on a TRANSIENT code, never a permanent rejection.
    /// </summary>
    Task<SteamTradeEligibilityResult> EvaluateAsync(User user, CancellationToken cancellationToken);
}

public enum SteamTradeEligibilityStatus
{
    /// <summary>Both conditions read and both satisfied.</summary>
    Eligible = 0,

    /// <summary>Steam reports the account as limited — remedy is a US$5 purchase.</summary>
    Limited = 1,

    /// <summary>Inside Steam's 15-day wait — remedy is time, so the caller reports the remainder.</summary>
    TooNew = 2,

    /// <summary>Could not be determined (Steam unreachable, or the account age was never captured).</summary>
    Unknown = 3,
}

/// <param name="RemainingDays">
/// Whole days left in the 15-day wait, set only for
/// <see cref="SteamTradeEligibilityStatus.TooNew"/>. Rounded UP: "0 days left"
/// while the account is still blocked would be a lie the UI cannot recover from.
/// </param>
public sealed record SteamTradeEligibilityResult(SteamTradeEligibilityStatus Status, int? RemainingDays)
{
    public static readonly SteamTradeEligibilityResult Eligible = new(SteamTradeEligibilityStatus.Eligible, null);
    public static readonly SteamTradeEligibilityResult Limited = new(SteamTradeEligibilityStatus.Limited, null);
    public static readonly SteamTradeEligibilityResult Unknown = new(SteamTradeEligibilityStatus.Unknown, null);

    public static SteamTradeEligibilityResult TooNew(int remainingDays) =>
        new(SteamTradeEligibilityStatus.TooNew, remainingDays);
}

/// <inheritdoc cref="ISteamTradeEligibilityChecker"/>
public sealed class SteamTradeEligibilityChecker : ISteamTradeEligibilityChecker
{
    /// <summary>
    /// Steam's own trade-eligibility wait for a new account, in days (08 §2.2a).
    ///
    /// <para>
    /// Deliberately a constant rather than a <c>SystemSetting</c>: this number
    /// is STEAM'S rule, not platform policy, and there is nothing for an
    /// operator to tune — a configurable copy would only invite someone to
    /// "relax" a limit we do not own and cannot relax. It is also NOT the same
    /// number as <c>auth.min_steam_account_age_days</c>, which is our own
    /// anti-fraud login gate and is a policy knob.
    /// </para>
    /// </summary>
    public const int SteamTradeEligibilityWaitDays = 15;

    private readonly ISteamAccountLimitedProbe _limitedProbe;
    private readonly TimeProvider _clock;
    private readonly ILogger<SteamTradeEligibilityChecker> _logger;

    public SteamTradeEligibilityChecker(
        ISteamAccountLimitedProbe limitedProbe,
        TimeProvider clock,
        ILogger<SteamTradeEligibilityChecker> logger)
    {
        _limitedProbe = limitedProbe;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SteamTradeEligibilityResult> EvaluateAsync(
        User user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // The age condition is evaluated FIRST because it costs nothing: it
        // reads a column, while the limited check spends one of the ten Steam
        // Community requests a minute allows. Same rule the accept pipeline
        // already follows — a request doomed by a cheaper condition never buys
        // a Steam round-trip.
        if (user.SteamAccountCreatedAt is not { } steamCreatedAt)
        {
            // NOT "old enough". The column is null only for accounts
            // provisioned before it existed; the value is refreshed on every
            // login, so the remedy is a sign-in. Treating null as eligible here
            // would rebuild the exact fail-open this round removes.
            _logger.LogInformation(
                "Steam account creation time missing for user {UserId} — trade eligibility unknown",
                user.Id);
            return SteamTradeEligibilityResult.Unknown;
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var eligibleAt = steamCreatedAt.AddDays(SteamTradeEligibilityWaitDays);
        if (nowUtc < eligibleAt)
        {
            var remaining = (int)Math.Ceiling((eligibleAt - nowUtc).TotalDays);
            return SteamTradeEligibilityResult.TooNew(Math.Max(remaining, 1));
        }

        var limited = await _limitedProbe.ProbeAsync(user.SteamId, cancellationToken);
        if (!limited.Available)
            return SteamTradeEligibilityResult.Unknown;

        return limited.IsLimited
            ? SteamTradeEligibilityResult.Limited
            : SteamTradeEligibilityResult.Eligible;
    }
}
