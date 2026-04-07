using Skinora.Shared.Enums;

namespace Skinora.Shared.Persistence.Outbox;

/// <summary>
/// Event outbox row — guarantees zero event loss across state transitions
/// (06 §3.18, 05 §5.1, 09 §9.3).
/// </summary>
/// <remarks>
/// <para>
/// The row is written in the same database transaction as the entity change
/// that produced the domain event. The Outbox Dispatcher (09 §13.4) polls
/// <c>PENDING</c> and <c>FAILED</c> rows together, dispatches the event to its
/// consumers and marks the row as <c>PROCESSED</c>.
/// </para>
/// <para>
/// <see cref="Id"/> doubles as the logical <c>EventId</c> referenced by
/// <see cref="ProcessedEvent.EventId"/> — there is no separate identifier
/// (09 §9.3 "Tek ID, tek otorite").
/// </para>
/// <para>
/// Status-dependent invariants enforced via CHECK constraint
/// (<see cref="Configurations.OutboxMessageConfiguration"/>):
/// <list type="bullet">
///   <item><c>PENDING</c>: <see cref="ProcessedAt"/> is <c>null</c>.</item>
///   <item><c>PROCESSED</c>: <see cref="ProcessedAt"/> is set.</item>
///   <item><c>FAILED</c>: <see cref="ProcessedAt"/> is <c>null</c> and
///         <see cref="ErrorMessage"/> is set.</item>
/// </list>
/// </para>
/// </remarks>
public class OutboxMessage
{
    /// <summary>
    /// Primary key. Doubles as the logical event identifier shared with
    /// consumer-side <see cref="ProcessedEvent.EventId"/>.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Fully qualified event type name (e.g. <c>TransactionCreatedEvent</c>).
    /// Used by the dispatcher to deserialize <see cref="Payload"/> into the
    /// concrete record type before publishing.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Serialized event payload (JSON).
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Current processing state.
    /// </summary>
    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.PENDING;

    /// <summary>
    /// Number of attempts the dispatcher has made on this row. Incremented on
    /// each failure. Once it reaches the configured max retry the row stays in
    /// <see cref="OutboxMessageStatus.FAILED"/> and an admin alert is raised
    /// (06 §3.18 retry semantiği).
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Last failure message (truncated to 2000 chars). NOT NULL when
    /// <see cref="Status"/> is <see cref="OutboxMessageStatus.FAILED"/>.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// UTC timestamp when the row was inserted (set by the producer in the
    /// same transaction as the entity change).
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the row reached <see cref="OutboxMessageStatus.PROCESSED"/>.
    /// Required to be NULL in any other status.
    /// </summary>
    public DateTime? ProcessedAt { get; set; }
}
