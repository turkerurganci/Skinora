using Skinora.Shared.Enums;
using Skinora.Users.Domain.Entities;

namespace Skinora.Platform.Application.UserSuspension;

/// <summary>
/// The one definition of what suspending an account writes: the four
/// <see cref="User"/> suspension fields, a <c>USER_BANNED</c> audit row and the
/// <c>AccountSuspendedEvent</c> that notifies the user (02 §14.0, 06 §3.1).
/// </summary>
/// <remarks>
/// <para>
/// Two callers suspend accounts and they must not drift: the admin endpoint
/// (<c>AdminUserSuspensionService</c>, T105a) and the 02 §14.2 non-delivery
/// rule, which suspends with no human in the loop. Before the second one
/// existed the three writes lived inline in the admin service; copying them
/// would have let a later change to "what a suspension is" reach only one
/// path. Validation stays with each caller — an admin's reason is typed and
/// length-checked, the system's is composed — only the effect is shared.
/// </para>
/// <para>
/// Stages only: the caller owns <c>SaveChangesAsync</c>, so the suspension
/// commits atomically with whatever produced it (for the non-delivery rule,
/// the terminal transition that crossed the threshold).
/// </para>
/// </remarks>
public interface IUserSuspensionWriter
{
    /// <summary>
    /// Stamps <paramref name="user"/> as suspended and stages the audit row and
    /// notification event. The caller must have checked
    /// <see cref="User.IsSuspended"/> first.
    /// </summary>
    /// <param name="user">A TRACKED user entity.</param>
    /// <param name="reason">Reason shown to the user and to admins.</param>
    /// <param name="expiresAt">Temporary-block expiry (UTC), or <c>null</c> for a suspension that lasts until an admin lifts it.</param>
    /// <param name="actorId">Admin id, or the system user for automated suspensions.</param>
    /// <param name="actorType"><see cref="ActorType.ADMIN"/> or <see cref="ActorType.SYSTEM"/>.</param>
    /// <param name="ipAddress">Caller IP for admin actions; <c>null</c> for system actions.</param>
    /// <param name="nowUtc">Suspension timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StageSuspensionAsync(
        User user,
        string reason,
        DateTime? expiresAt,
        Guid actorId,
        ActorType actorType,
        string? ipAddress,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}
