using System.Linq.Expressions;
using Hangfire;
using Hangfire.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;

namespace Skinora.Notifications.Infrastructure.DeliveryJobs;

/// <summary>
/// Hangfire job that runs a single <see cref="NotificationDelivery"/> through
/// its <see cref="INotificationChannelHandler"/> and records the outcome
/// (05 §7.5 retry policy; 08 §4.3 deferred-tier escalation introduced by T78).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two-tier retry policy (T78):</b>
/// </para>
/// <list type="bullet">
///   <item><b>Immediate tier (this job):</b> initial run + Hangfire
///         <see cref="AutomaticRetryAttribute"/> retries at 1 dk / 5 dk / 15 dk
///         (matches 05 §7.5). On the final transient failure the row is
///         flipped to <see cref="DeliveryStatus.DEFERRED"/> and a
///         <see cref="DeferredNotificationDeliveryJob"/> is scheduled 30 dk
///         out — the throw is swallowed so Hangfire records "Succeeded"
///         (the deferred tier owns the retry from here).</item>
///   <item><b>Deferred tier:</b> three further attempts at 30 dk / 1 sa /
///         4 sa managed explicitly by <see cref="DeferredNotificationDeliveryJob"/>.</item>
/// </list>
/// <para>
/// <b>Exception classification:</b>
/// </para>
/// <list type="bullet">
///   <item><see cref="PermanentChannelDeliveryException"/> — flip to FAILED,
///         fire admin alert, swallow the throw (no retry, no deferred).</item>
///   <item><see cref="TransientChannelDeliveryException"/> — re-throw on
///         intermediate attempts so Hangfire retries; on the final attempt
///         flip to DEFERRED + schedule tier 1.</item>
///   <item>Any other <see cref="Exception"/> — conservatively treated as
///         transient (mirrors pre-T78 behaviour so untouched stub
///         channels keep working).</item>
/// </list>
/// </remarks>
[AutomaticRetry(
    Attempts = MaxRetryAttempts,
    DelaysInSeconds = new[] { 60, 300, 900 },
    OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed class NotificationDeliveryJob
{
    /// <summary>
    /// Number of automatic retries. With Hangfire semantics this means total
    /// attempts = 1 (initial) + 3 (retries) = 4 — matched 1 : 1 to the three
    /// backoff entries in 05 §7.5.
    /// </summary>
    public const int MaxRetryAttempts = 3;

    /// <summary>
    /// Delay between the immediate-tier final attempt and the first
    /// deferred-tier attempt (08 §4.3 — 30 dakika).
    /// </summary>
    internal static readonly TimeSpan FirstDeferredDelay = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _dbContext;
    private readonly IEnumerable<INotificationChannelHandler> _channelHandlers;
    private readonly INotificationTemplateResolver _templateResolver;
    private readonly INotificationAdminAlertSink _alertSink;
    private readonly IBackgroundJobScheduler _jobScheduler;
    private readonly ILogger<NotificationDeliveryJob> _logger;

    public NotificationDeliveryJob(
        AppDbContext dbContext,
        IEnumerable<INotificationChannelHandler> channelHandlers,
        INotificationTemplateResolver templateResolver,
        INotificationAdminAlertSink alertSink,
        IBackgroundJobScheduler jobScheduler,
        ILogger<NotificationDeliveryJob> logger)
    {
        _dbContext = dbContext;
        _channelHandlers = channelHandlers;
        _templateResolver = templateResolver;
        _alertSink = alertSink;
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    /// <summary>
    /// Hangfire entry point — its expression serializer requires a synchronous
    /// signature (T32 RefreshTokenCleanupJob mirrors this shape). The core
    /// work lives in <see cref="RunAsync"/> so integration tests can inject an
    /// explicit attempt number without instantiating a Hangfire
    /// <see cref="PerformContext"/>.
    /// </summary>
    public void Execute(Guid deliveryId, PerformContext? context)
    {
        var cancellationToken = context?.CancellationToken?.ShutdownToken
                                ?? CancellationToken.None;
        var attemptNumber = (context?.GetJobParameter<int>("RetryCount") ?? 0) + 1;

        RunAsync(deliveryId, attemptNumber, cancellationToken).GetAwaiter().GetResult();
    }

    public async Task RunAsync(
        Guid deliveryId,
        int attemptNumber,
        CancellationToken cancellationToken)
    {
        var delivery = await _dbContext.Set<NotificationDelivery>()
            .FirstOrDefaultAsync(d => d.Id == deliveryId, cancellationToken);

        if (delivery is null)
        {
            // Producer transaction never committed (09 §13.3) or the row was
            // archived. Either way, nothing to do.
            _logger.LogWarning(
                "NotificationDelivery {DeliveryId} not found — job skipped.",
                deliveryId);
            return;
        }

        if (delivery.Status == DeliveryStatus.SENT)
        {
            // Idempotency at row level — duplicate enqueue or replay should
            // not trigger a second external send.
            return;
        }

        var notification = await _dbContext.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == delivery.NotificationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Notification {delivery.NotificationId} referenced by delivery {delivery.Id} is missing.");

        var handler = _channelHandlers.FirstOrDefault(h => h.Channel == delivery.Channel)
            ?? throw new InvalidOperationException(
                $"No INotificationChannelHandler registered for channel {delivery.Channel}.");

        delivery.AttemptCount += 1;

        var rendered = new RenderedNotificationTemplate(notification.Title, notification.Body);

        try
        {
            await handler.SendAsync(delivery.TargetExternalId, rendered, cancellationToken);

            delivery.Status = DeliveryStatus.SENT;
            delivery.SentAt = DateTime.UtcNow;
            delivery.LastError = null;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (PermanentChannelDeliveryException pex)
        {
            // 422 / auth / config errors — retrying cannot help. Flip the row
            // to FAILED, fire the alert, and swallow so Hangfire does not
            // schedule the next retry attempt.
            delivery.Status = DeliveryStatus.FAILED;
            delivery.LastError = Truncate(pex.Message, 1000);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                pex,
                "Notification delivery {DeliveryId} permanently failed on attempt {Attempt}.",
                delivery.Id,
                attemptNumber);

            await _alertSink.RaiseDeliveryExhaustedAsync(delivery, cancellationToken);
        }
        catch (Exception ex)
        {
            // Transient failure (or an unknown exception type that we
            // conservatively treat as transient — pre-T78 behaviour).
            var isFinalAttempt = attemptNumber > MaxRetryAttempts;

            if (isFinalAttempt)
            {
                // Immediate-tier budget exhausted — hand the row off to the
                // deferred tier (30 dk / 1 sa / 4 sa per 08 §4.3). Swallow
                // the throw so Hangfire marks this job "Succeeded"; the
                // deferred-tier job is the sole retry authority from here.
                delivery.Status = DeliveryStatus.DEFERRED;
                delivery.LastError = Truncate(ex.Message, 1000);
                await _dbContext.SaveChangesAsync(cancellationToken);

                Expression<Action<DeferredNotificationDeliveryJob>> call =
                    job => job.Execute(delivery.Id, DeferredNotificationDeliveryJob.FirstTier, null!);

                var jobId = _jobScheduler.Schedule(call, FirstDeferredDelay);

                _logger.LogWarning(
                    ex,
                    "Notification delivery {DeliveryId} exhausted immediate retries — deferred (tier 1, jobId={JobId}).",
                    delivery.Id,
                    jobId);

                return;
            }

            delivery.Status = DeliveryStatus.FAILED;
            delivery.LastError = Truncate(ex.Message, 1000);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                ex,
                "Notification delivery {DeliveryId} failed on attempt {Attempt}/{MaxAttempt}.",
                delivery.Id,
                attemptNumber,
                MaxRetryAttempts + 1);

            // Re-throw so Hangfire's AutomaticRetry pipeline picks the row up
            // for the next backoff window.
            throw;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
