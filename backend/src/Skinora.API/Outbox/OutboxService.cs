using System.Text.Json;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;

namespace Skinora.API.Outbox;

/// <summary>
/// Default <see cref="IOutboxService"/> — writes a row to the
/// <c>OutboxMessages</c> table inside the caller's database transaction
/// (05 §5.1, 09 §9.3 — atomik commit garantisi).
/// </summary>
/// <remarks>
/// <para>
/// The implementation only adds the row to the EF change tracker; it
/// deliberately does <b>not</b> call <c>SaveChangesAsync</c>. The caller's
/// unit of work commits the entity change and the outbox row in a single DB
/// transaction so that either both land or neither does.
/// </para>
/// <para>
/// <see cref="OutboxMessage.Id"/> is set from <see cref="IDomainEvent.EventId"/>
/// — the same identifier flows through to consumer-side
/// <c>ProcessedEvents</c> rows (09 §9.3 "Tek ID, tek otorite").
/// </para>
/// </remarks>
public class OutboxService : IOutboxService
{
    /// <summary>
    /// JSON options used to serialize event payloads. Cached on a static
    /// field so the dispatcher can mirror the same settings during
    /// deserialization.
    /// </summary>
    public static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        PropertyNamingPolicy = null,           // preserve C# property casing for round-trip stability
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    private readonly AppDbContext _dbContext;

    public OutboxService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var concreteType = domainEvent.GetType();

        var message = new OutboxMessage
        {
            Id = domainEvent.EventId,
            EventType = OutboxEventTypeName.For(concreteType),
            Payload = JsonSerializer.Serialize(domainEvent, concreteType, PayloadSerializerOptions),
            Status = OutboxMessageStatus.PENDING,
            RetryCount = 0,
            ErrorMessage = null,
            CreatedAt = DateTime.UtcNow,
            ProcessedAt = null,
        };

        _dbContext.OutboxMessages.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stable, version-agnostic encoding of a CLR type name used in
/// <see cref="OutboxMessage.EventType"/>. Uses
/// <c>FullName, AssemblyName</c> rather than <c>AssemblyQualifiedName</c> so
/// minor assembly version bumps do not invalidate persisted rows.
/// </summary>
public static class OutboxEventTypeName
{
    public static string For(Type type)
        => $"{type.FullName}, {type.Assembly.GetName().Name}";

    public static Type? Resolve(string typeName)
        => Type.GetType(typeName, throwOnError: false);
}
