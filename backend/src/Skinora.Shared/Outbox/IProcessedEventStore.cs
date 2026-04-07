namespace Skinora.Shared.Outbox;

/// <summary>
/// Consumer idempotency tracker — backed by the <c>ProcessedEvents</c> table
/// (06 §3.19, 09 §9.3).
/// </summary>
/// <remarks>
/// <para>
/// Consumers call <see cref="ExistsAsync"/> before performing the side effect
/// to skip events that have already been processed by the same consumer name,
/// then call <see cref="MarkAsProcessedAsync"/> after the side effect so a
/// retry of the same event short-circuits.
/// </para>
/// <para>
/// <b>Atomicity contract:</b> <see cref="MarkAsProcessedAsync"/> only adds
/// the row to the change tracker — it does <b>not</b> call
/// <c>SaveChangesAsync</c>. The caller commits both the side-effect entity
/// changes and the <c>ProcessedEvent</c> row in the same database transaction
/// (09 §9.3).
/// </para>
/// </remarks>
public interface IProcessedEventStore
{
    /// <summary>
    /// Returns <c>true</c> if the consumer has already processed an event
    /// with this <paramref name="eventId"/>.
    /// </summary>
    Task<bool> ExistsAsync(
        Guid eventId,
        string consumerName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a <c>ProcessedEvent</c> row to the change tracker; the caller
    /// must call <c>SaveChangesAsync</c> to commit it together with the
    /// consumer's side-effect changes.
    /// </summary>
    Task MarkAsProcessedAsync(
        Guid eventId,
        string consumerName,
        CancellationToken cancellationToken = default);
}
