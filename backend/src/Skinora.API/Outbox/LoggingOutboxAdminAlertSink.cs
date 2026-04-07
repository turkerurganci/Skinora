using Microsoft.Extensions.Logging;
using Skinora.Shared.Outbox;
using Skinora.Shared.Persistence.Outbox;

namespace Skinora.API.Outbox;

/// <summary>
/// Default <see cref="IOutboxAdminAlertSink"/> — logs an error and stops.
/// </summary>
/// <remarks>
/// T10 ships only the logging fallback so the dispatcher's max-retry path is
/// observable end-to-end. The real admin notification wiring lands with the
/// notification module (T37 onward) by registering a different
/// implementation in DI.
/// </remarks>
public class LoggingOutboxAdminAlertSink : IOutboxAdminAlertSink
{
    private readonly ILogger<LoggingOutboxAdminAlertSink> _logger;

    public LoggingOutboxAdminAlertSink(ILogger<LoggingOutboxAdminAlertSink> logger)
    {
        _logger = logger;
    }

    public Task RaiseMaxRetryExceededAsync(
        OutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        _logger.LogError(
            "Outbox event {EventId} ({EventType}) exhausted retries ({RetryCount}). " +
            "Last error: {ErrorMessage}",
            message.Id,
            message.EventType,
            message.RetryCount,
            message.ErrorMessage);

        return Task.CompletedTask;
    }
}
