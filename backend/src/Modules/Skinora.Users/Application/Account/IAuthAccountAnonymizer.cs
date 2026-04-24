namespace Skinora.Users.Application.Account;

/// <summary>
/// Ports the auth-side session cleanup required by 06 §6.2:
/// <list type="bullet">
///   <item><description>Deactivation — every non-revoked <c>RefreshToken</c>
///   for the user is revoked so the outstanding session is killed (02 §19
///   "Oturum sonlandırılır"; 07 §5.17 U13).</description></item>
///   <item><description>Deletion — in addition, each <c>RefreshToken</c> is
///   soft-deleted and its <c>DeviceInfo</c>/<c>IpAddress</c> fields are
///   cleared (they are PII snapshots of the originating device).</description></item>
/// </list>
/// Implementation lives in <c>Skinora.Auth</c> because that module owns the
/// entity; the interface lives in <c>Skinora.Users</c> so the lifecycle
/// service stays off the Auth reference graph.
/// </summary>
public interface IAuthAccountAnonymizer
{
    /// <summary>
    /// Revokes every non-revoked refresh token for the user. Used by the
    /// deactivate flow where the token rows themselves must survive so the
    /// user can observe that the session was ended on re-login.
    /// </summary>
    Task<int> RevokeAllSessionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes + soft-deletes every refresh token and clears
    /// <c>DeviceInfo</c>/<c>IpAddress</c>. Used by the delete flow.
    /// </summary>
    Task<int> AnonymizeSessionsAsync(Guid userId, CancellationToken cancellationToken);
}
