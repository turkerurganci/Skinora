using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the T105a admin suspension service after a user account has been
/// suspended via <c>POST /admin/users/:userId/suspend</c> (02 §14.0/§16.2,
/// 03 §8.3). The Notifications consumer fans out an <c>ACCOUNT_SUSPENDED</c>
/// notification to the affected user.
/// </summary>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="UserId">The suspended user.</param>
/// <param name="Reason">Admin-supplied reason (≥10 chars).</param>
/// <param name="ExpiresAt">Temporary-block expiry (UTC), or <c>null</c> for a permanent suspension.</param>
/// <param name="OccurredAt">UTC timestamp the suspension was committed.</param>
public record AccountSuspendedEvent(
    Guid EventId,
    Guid UserId,
    string Reason,
    DateTime? ExpiresAt,
    DateTime OccurredAt) : IDomainEvent;
