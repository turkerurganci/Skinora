namespace Skinora.Shared.Persistence.Outbox;

/// <summary>
/// Consumer idempotency marker — prevents the same outbox event from being
/// processed twice by the same consumer (06 §3.19, 05 §5.1, 09 §9.3).
/// </summary>
/// <remarks>
/// <para>
/// A row is inserted by a consumer in the same database transaction as the
/// side effect it performs (when the side effect is itself transactional).
/// The unique constraint on <c>(EventId, ConsumerName)</c> guarantees that a
/// retry of the same event into the same consumer is rejected at the database
/// level rather than relying on application-level checks alone.
/// </para>
/// <para>
/// <b>FK note (06 §3.19):</b> <see cref="EventId"/> is a logical reference to
/// <see cref="OutboxMessage.Id"/>; no database-level foreign key is defined
/// because both tables are retention-cleaned (30 days) and FK constraints
/// would force a deletion ordering between the two batch jobs.
/// </para>
/// </remarks>
public class ProcessedEvent
{
    public Guid Id { get; set; }

    /// <summary>
    /// Logical reference to <see cref="OutboxMessage.Id"/> — i.e. the
    /// <c>EventId</c> of the dispatched domain event.
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// Stable consumer identifier (e.g. <c>NotificationConsumer</c>). The same
    /// event may be processed once per distinct consumer, so the unique
    /// constraint includes both columns.
    /// </summary>
    public string ConsumerName { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the consumer finished processing the event.
    /// </summary>
    public DateTime ProcessedAt { get; set; }
}
