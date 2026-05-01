using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;

namespace Skinora.Platform.Application.Audit;

/// <inheritdoc cref="IAuditLogger"/>
public sealed class AuditLogger : IAuditLogger
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public AuditLogger(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken)
    {
        ValidateActor(entry);
        ValidateRequiredStrings(entry);

        _db.Set<AuditLog>().Add(new AuditLog
        {
            UserId = entry.UserId,
            ActorId = entry.ActorId,
            ActorType = entry.ActorType,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            OldValue = entry.OldValue,
            NewValue = entry.NewValue,
            IpAddress = entry.IpAddress,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        });

        return Task.CompletedTask;
    }

    private static void ValidateActor(AuditLogEntry entry)
    {
        switch (entry.ActorType)
        {
            case ActorType.SYSTEM:
                if (entry.ActorId != SeedConstants.SystemUserId)
                    throw new InvalidActorException(
                        $"ActorType.SYSTEM requires ActorId = SystemUserId " +
                        $"({SeedConstants.SystemUserId}); got {entry.ActorId}.");
                break;

            case ActorType.ADMIN:
            case ActorType.USER:
                if (entry.ActorId == Guid.Empty)
                    throw new InvalidActorException(
                        $"ActorType.{entry.ActorType} requires a non-empty ActorId (06 §8.6a).");
                if (entry.ActorId == SeedConstants.SystemUserId)
                    throw new InvalidActorException(
                        $"ActorType.{entry.ActorType} cannot use the SYSTEM Guid " +
                        $"({SeedConstants.SystemUserId}); reserved for ActorType.SYSTEM.");
                break;

            default:
                throw new InvalidActorException(
                    $"Unknown ActorType '{entry.ActorType}'.");
        }
    }

    private static void ValidateRequiredStrings(AuditLogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.EntityType))
            throw new InvalidActorException(
                "AuditLogEntry.EntityType is required (06 §3.20).");
        if (string.IsNullOrWhiteSpace(entry.EntityId))
            throw new InvalidActorException(
                "AuditLogEntry.EntityId is required (06 §3.20).");
    }
}
