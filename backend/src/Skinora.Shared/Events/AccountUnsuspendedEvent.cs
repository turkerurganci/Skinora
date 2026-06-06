using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the T105a admin suspension service after a user account has been
/// un-suspended — either by an admin via <c>DELETE /admin/users/:userId/suspend</c>
/// or automatically by the <c>AutoUnsuspendJob</c> when a temporary block
/// expires (02 §14.0/§16.2). The Notifications consumer fans out an
/// <c>ACCOUNT_UNSUSPENDED</c> notification to the affected user.
/// </summary>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="UserId">The un-suspended user.</param>
/// <param name="Automatic"><c>true</c> when lifted automatically (temp-block expiry); <c>false</c> for an admin action.</param>
/// <param name="OccurredAt">UTC timestamp the un-suspension was committed.</param>
public record AccountUnsuspendedEvent(
    Guid EventId,
    Guid UserId,
    bool Automatic,
    DateTime OccurredAt) : IDomainEvent;
