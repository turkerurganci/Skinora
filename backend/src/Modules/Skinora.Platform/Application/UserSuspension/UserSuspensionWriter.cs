using System.Text.Json;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Users.Domain.Entities;

namespace Skinora.Platform.Application.UserSuspension;

/// <inheritdoc cref="IUserSuspensionWriter"/>
/// <remarks>
/// Suspension does NOT block login (unlike <c>IsDeactivated</c>) — enforcement
/// is at the fund-flow mutation services and the <c>/auth/me</c>
/// <c>isSuspended</c> flag. The audit action is <see cref="AuditAction.USER_BANNED"/>
/// for both actors, so the audit trail answers "who suspended this account"
/// from <c>ActorType</c> rather than from two differently named actions.
/// </remarks>
public sealed class UserSuspensionWriter : IUserSuspensionWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly IAuditLogger _audit;
    private readonly IOutboxService _outbox;

    public UserSuspensionWriter(IAuditLogger audit, IOutboxService outbox)
    {
        _audit = audit;
        _outbox = outbox;
    }

    public async Task StageSuspensionAsync(
        User user,
        string reason,
        DateTime? expiresAt,
        Guid actorId,
        ActorType actorType,
        string? ipAddress,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        user.IsSuspended = true;
        user.SuspendedAt = nowUtc;
        user.SuspensionReason = reason;
        user.SuspensionExpiresAt = expiresAt;

        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: user.Id,
                ActorId: actorId,
                ActorType: actorType,
                Action: AuditAction.USER_BANNED,
                EntityType: nameof(User),
                EntityId: user.Id.ToString(),
                OldValue: null,
                NewValue: JsonSerializer.Serialize(new
                {
                    Reason = reason,
                    ExpiresAt = expiresAt,
                }, JsonOptions),
                IpAddress: ipAddress),
            cancellationToken);

        await _outbox.PublishAsync(
            new AccountSuspendedEvent(
                EventId: Guid.NewGuid(),
                UserId: user.Id,
                Reason: reason,
                ExpiresAt: expiresAt,
                OccurredAt: nowUtc),
            cancellationToken);
    }
}
