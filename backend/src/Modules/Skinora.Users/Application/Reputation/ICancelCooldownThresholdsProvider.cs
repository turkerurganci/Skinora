namespace Skinora.Users.Application.Reputation;

/// <summary>
/// Reads <c>cancel_limit_count</c>, <c>cancel_limit_period_hours</c> and
/// <c>cancel_cooldown_hours</c> SystemSettings (06 §3.17 / 02 §14.2). Kept as
/// a separate port from <see cref="IReputationThresholdsProvider"/> so the
/// reputation read path does not pull cancel-cooldown wiring into pure
/// score-display contexts.
/// </summary>
public interface ICancelCooldownThresholdsProvider
{
    Task<CancelCooldownThresholds> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Snapshot of the three cancel-cooldown SystemSettings.</summary>
/// <param name="LimitCount">cancel_limit_count — responsible cancellations allowed within the window.</param>
/// <param name="WindowHours">cancel_limit_period_hours — rolling window length.</param>
/// <param name="CooldownHours">cancel_cooldown_hours — duration of the resulting block.</param>
public sealed record CancelCooldownThresholds(int LimitCount, int WindowHours, int CooldownHours);
