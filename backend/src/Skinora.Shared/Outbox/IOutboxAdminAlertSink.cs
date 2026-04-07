using Skinora.Shared.Persistence.Outbox;

namespace Skinora.Shared.Outbox;

/// <summary>
/// Hook invoked when an outbox row exhausts its retry budget — the platform
/// must surface a max-retry alert to operators (06 §3.18, 05 §5.1).
/// </summary>
/// <remarks>
/// T10 ships a default <c>LoggingOutboxAdminAlertSink</c> that emits an error
/// log; the formal admin notification path is wired up later (T37 onward) by
/// registering a different implementation via DI.
/// </remarks>
public interface IOutboxAdminAlertSink
{
    Task RaiseMaxRetryExceededAsync(
        OutboxMessage message,
        CancellationToken cancellationToken = default);
}
