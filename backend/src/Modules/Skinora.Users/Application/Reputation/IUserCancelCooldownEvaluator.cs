namespace Skinora.Users.Application.Reputation;

/// <summary>
/// Evaluates the 02 §14.2 cancellation cooldown rule: when a user accumulates
/// more than <c>cancel_limit_count</c> responsible cancellations within the
/// rolling <c>cancel_limit_period_hours</c> window, sets
/// <c>User.CooldownExpiresAt = now + cancel_cooldown_hours</c>.
/// </summary>
/// <remarks>
/// Designed to be invoked by future state-machine hooks (T44+) on every
/// CANCELLED_SELLER / CANCELLED_BUYER / CANCELLED_TIMEOUT-by-user
/// transition. Idempotent: re-running with the same DB state produces the
/// same outcome.
/// </remarks>
public interface IUserCancelCooldownEvaluator
{
    /// <summary>
    /// Re-evaluate cooldown for the given user. Mutates the tracked
    /// <c>User.CooldownExpiresAt</c> when the limit is exceeded; leaves it
    /// untouched (does NOT clear stale cooldowns) otherwise. Caller owns the
    /// surrounding <c>SaveChangesAsync</c>.
    /// </summary>
    Task<CooldownEvaluationResult> EvaluateAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Outcome of <see cref="IUserCancelCooldownEvaluator.EvaluateAsync"/>.</summary>
/// <param name="ResponsibleCancelCount">Cancellations attributable to the user inside the window.</param>
/// <param name="LimitCount">Configured <c>cancel_limit_count</c>.</param>
/// <param name="WindowHours">Configured <c>cancel_limit_period_hours</c>.</param>
/// <param name="NewCooldownExpiresAt">Set when the limit was exceeded; null otherwise.</param>
public sealed record CooldownEvaluationResult(
    int ResponsibleCancelCount,
    int LimitCount,
    int WindowHours,
    DateTime? NewCooldownExpiresAt);
