namespace Skinora.Shared.Outbox;

/// <summary>
/// Self-rescheduling Hangfire job that drains the outbox table
/// (09 §13.4, 05 §5.1).
/// </summary>
/// <remarks>
/// <para>
/// The implementation acquires a distributed lock to guarantee a single
/// active dispatcher chain across instances/restarts, processes one batch of
/// <c>PENDING</c> and <c>FAILED</c> rows, and re-enqueues itself with a
/// configurable delay regardless of the batch outcome (try/finally
/// reschedule — 09 §13.4).
/// </para>
/// </remarks>
public interface IOutboxDispatcher
{
    /// <summary>
    /// Process the next batch of outbox rows and schedule the next iteration.
    /// Called via Hangfire — Hangfire serializes the expression so the method
    /// must remain a parameterless instance entry point that resolves
    /// dependencies from DI.
    /// </summary>
    Task ProcessAndRescheduleAsync();
}
