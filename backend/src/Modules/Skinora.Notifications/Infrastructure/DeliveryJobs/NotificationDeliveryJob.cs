using Hangfire;
using Hangfire.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;

namespace Skinora.Notifications.Infrastructure.DeliveryJobs;

/// <summary>
/// Hangfire job that runs a single <see cref="NotificationDelivery"/> through
/// its <see cref="INotificationChannelHandler"/> and records the outcome
/// (05 §7.5 retry policy).
/// </summary>
/// <remarks>
/// <para>
/// Retry policy is class-level via <see cref="AutomaticRetryAttribute"/> with
/// the exponential backoff schedule from 05 §7.5
/// (<c>1 dk → 5 dk → 15 dk</c>). Hangfire's <c>Attempts</c> parameter counts
/// retries excluding the initial run, so <c>Attempts = 3</c> +
/// <c>DelaysInSeconds = [60, 300, 900]</c> yields four total attempts:
/// initial + three exponentially-spaced retries. <c>OnAttemptsExceeded =
/// Fail</c> keeps the failed job visible in the Hangfire dashboard for
/// post-mortem; the admin alert sink fires from inside the catch block on
/// the final attempt so the alert lands regardless of dashboard state.
/// </para>
/// <para>
/// The job is idempotent at row level: every attempt re-loads the
/// <see cref="NotificationDelivery"/> row, refuses to act on a row already
/// in <see cref="DeliveryStatus.SENT"/>, and otherwise updates
/// <see cref="NotificationDelivery.AttemptCount"/> /
/// <see cref="NotificationDelivery.Status"/> /
/// <see cref="NotificationDelivery.LastError"/> as a single audit step
/// (06 §3.13a). A missing row is treated as a no-op (09 §13.3 atomicity
/// boundary — the producer transaction never committed).
/// </para>
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

    private readonly AppDbContext _dbContext;
    private readonly IEnumerable<INotificationChannelHandler> _channelHandlers;
    private readonly INotificationTemplateResolver _templateResolver;
    private readonly INotificationAdminAlertSink _alertSink;
    private readonly ILogger<NotificationDeliveryJob> _logger;

    public NotificationDeliveryJob(
        AppDbContext dbContext,
        IEnumerable<INotificationChannelHandler> channelHandlers,
        INotificationTemplateResolver templateResolver,
        INotificationAdminAlertSink alertSink,
        ILogger<NotificationDeliveryJob> logger)
    {
        _dbContext = dbContext;
        _channelHandlers = channelHandlers;
        _templateResolver = templateResolver;
        _alertSink = alertSink;
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
        catch (Exception ex)
        {
            delivery.Status = DeliveryStatus.FAILED;
            delivery.LastError = Truncate(ex.Message, 1000);

            await _dbContext.SaveChangesAsync(cancellationToken);

            var isFinalAttempt = attemptNumber > MaxRetryAttempts;

            _logger.LogWarning(
                ex,
                "Notification delivery {DeliveryId} failed on attempt {Attempt}/{MaxAttempt}.",
                delivery.Id,
                attemptNumber,
                MaxRetryAttempts + 1);

            if (isFinalAttempt)
            {
                await _alertSink.RaiseDeliveryExhaustedAsync(delivery, cancellationToken);
            }

            // Re-throw so Hangfire's AutomaticRetry pipeline picks the row up
            // for the next backoff window — or marks it Failed when the
            // budget is exhausted.
            throw;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
