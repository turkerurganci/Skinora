using System.Text.Json;
using MediatR;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Outbox;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;

namespace Skinora.API.Outbox;

/// <summary>
/// Default <see cref="IOutboxDispatcher"/> — self-rescheduling Hangfire job
/// that drains the <c>OutboxMessages</c> table (09 §13.4, 05 §5.1).
/// </summary>
/// <remarks>
/// <para>
/// Each invocation acquires a Medallion distributed lock named
/// <c>outbox-dispatcher</c> with a non-blocking timeout. If another instance
/// holds the lock the iteration sits out and the rescheduled chain continues
/// — duplicate dispatcher chains caused by restarts or multi-instance
/// deployments collapse to one (09 §13.4 tekillik garantisi).
/// </para>
/// <para>
/// The batch query selects <c>PENDING</c> and <c>FAILED</c> rows together
/// (06 §3.18 retry semantiği). Each row is deserialized using its persisted
/// <see cref="OutboxMessage.EventType"/>, published via
/// <see cref="IPublisher.Publish(object, CancellationToken)"/>, and the
/// status is flipped to <c>PROCESSED</c>. On exception the row's status is
/// set to <c>FAILED</c>, <see cref="OutboxMessage.RetryCount"/> is
/// incremented, and once the configured max retry is reached the
/// <see cref="IOutboxAdminAlertSink"/> hook is invoked.
/// </para>
/// <para>
/// Rescheduling is wrapped in a <c>try/finally</c> so an exception in the
/// batch never breaks the chain (09 §13.4 hata dayanıklılığı).
/// </para>
/// </remarks>
public class OutboxDispatcher : IOutboxDispatcher
{
    /// <summary>
    /// Distributed lock name. Public so tests and operators can reference it.
    /// </summary>
    public const string LockName = "outbox-dispatcher";

    private readonly AppDbContext _dbContext;
    private readonly IPublisher _publisher;
    private readonly IOutboxAdminAlertSink _alertSink;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IBackgroundJobScheduler _jobScheduler;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        AppDbContext dbContext,
        IPublisher publisher,
        IOutboxAdminAlertSink alertSink,
        IDistributedLockProvider lockProvider,
        IBackgroundJobScheduler jobScheduler,
        IOptions<OutboxOptions> options,
        ILogger<OutboxDispatcher> logger)
    {
        _dbContext = dbContext;
        _publisher = publisher;
        _alertSink = alertSink;
        _lockProvider = lockProvider;
        _jobScheduler = jobScheduler;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessAndRescheduleAsync()
    {
        try
        {
            var lockTimeout = TimeSpan.FromSeconds(_options.LockAcquireTimeoutSeconds);

            await using var handle = await _lockProvider
                .CreateLock(LockName)
                .TryAcquireAsync(lockTimeout, CancellationToken.None);

            if (handle is null)
            {
                _logger.LogDebug("Outbox dispatcher skipped — distributed lock held by another instance.");
                return;
            }

            await ProcessBatchAsync();
        }
        catch (Exception ex)
        {
            // Swallow at the chain boundary so the finally block can still
            // schedule the next iteration. Per-message failures are handled
            // inside ProcessBatchAsync; this catch only fires for catastrophic
            // failures (DB unreachable, lock provider crash etc.).
            _logger.LogError(ex, "Outbox dispatcher iteration failed.");
        }
        finally
        {
            try
            {
                _jobScheduler.Schedule<IOutboxDispatcher>(
                    d => d.ProcessAndRescheduleAsync(),
                    TimeSpan.FromSeconds(_options.PollingIntervalSeconds));
            }
            catch (Exception scheduleEx)
            {
                // Last-resort: log and stop. The OutboxStartupHook will
                // re-prime the chain on the next process start.
                _logger.LogCritical(
                    scheduleEx,
                    "Outbox dispatcher could not reschedule itself — chain broken until restart.");
            }
        }
    }

    private async Task ProcessBatchAsync()
    {
        var batch = await _dbContext.OutboxMessages
            .Where(m => m.Status == OutboxMessageStatus.PENDING
                        || m.Status == OutboxMessageStatus.FAILED)
            .OrderBy(m => m.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync();

        if (batch.Count == 0)
            return;

        foreach (var message in batch)
        {
            try
            {
                var concreteType = OutboxEventTypeName.Resolve(message.EventType)
                    ?? throw new InvalidOperationException(
                        $"Unknown outbox event type '{message.EventType}'.");

                var deserialized = JsonSerializer.Deserialize(
                    message.Payload,
                    concreteType,
                    OutboxService.PayloadSerializerOptions)
                    ?? throw new InvalidOperationException(
                        $"Outbox payload for event {message.Id} ({message.EventType}) deserialized to null.");

                if (deserialized is not IDomainEvent)
                {
                    throw new InvalidOperationException(
                        $"Outbox event type '{message.EventType}' does not implement IDomainEvent.");
                }

                // IPublisher.Publish(object) dispatches by runtime type so
                // every INotificationHandler<TConcreteEvent> registered in
                // DI receives the call.
                await _publisher.Publish(deserialized);

                message.Status = OutboxMessageStatus.PROCESSED;
                message.ProcessedAt = DateTime.UtcNow;
                message.ErrorMessage = null;
            }
            catch (Exception ex)
            {
                message.Status = OutboxMessageStatus.FAILED;
                message.ErrorMessage = Truncate(ex.ToString(), 2000);
                message.RetryCount += 1;

                _logger.LogWarning(
                    ex,
                    "Outbox event {EventId} ({EventType}) failed on attempt {RetryCount}/{MaxRetry}.",
                    message.Id,
                    message.EventType,
                    message.RetryCount,
                    _options.MaxRetryCount);

                if (message.RetryCount >= _options.MaxRetryCount)
                {
                    try
                    {
                        await _alertSink.RaiseMaxRetryExceededAsync(message);
                    }
                    catch (Exception alertEx)
                    {
                        // Alert sink failure must not abort the batch.
                        _logger.LogError(
                            alertEx,
                            "Outbox admin alert sink failed for event {EventId}.",
                            message.Id);
                    }
                }
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
