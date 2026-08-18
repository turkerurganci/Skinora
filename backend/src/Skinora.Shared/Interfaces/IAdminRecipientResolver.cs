namespace Skinora.Shared.Interfaces;

/// <summary>
/// Resolves the set of admin user ids that should receive an admin-targeted
/// in-app notification (06 §2.13 "Admin" target types — <c>ADMIN_FLAG_ALERT</c>,
/// <c>ADMIN_ESCALATION</c>, <c>ADMIN_PAYMENT_FAILURE</c>).
/// </summary>
/// <remarks>
/// WP8 fans admin alerts out to every user holding an admin role (owner
/// decision: broadcast-to-all-admins, mirroring the existing realtime admin
/// broadcast on <c>/hubs/admin</c>). The implementation lives in the Admin
/// module — the only module that owns the <c>AdminUserRole</c> assignment
/// table — so the Notifications-module consumers depend solely on this Shared
/// abstraction and no Notifications → Admin module reference is introduced.
/// </remarks>
public interface IAdminRecipientResolver
{
    /// <summary>
    /// Returns the distinct user ids of every active admin (a user with at
    /// least one non-deleted <c>AdminUserRole</c> assignment). Returns an
    /// empty list when no admins exist — callers translate that to "no
    /// in-app fan-out" rather than an error.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAdminUserIdsAsync(CancellationToken cancellationToken);
}
