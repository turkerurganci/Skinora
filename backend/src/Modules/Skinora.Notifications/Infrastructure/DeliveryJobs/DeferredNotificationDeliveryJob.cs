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
/// Deferred-tier retry job for notifications whose immediate retry
/// budget (1 dk / 5 dk / 15 dk via <see cref="NotificationDeliveryJob"/>)
/// was exhausted by a transient failure. Schedules three explicit
/// attempts at 30 dk / 1 sa / 4 sa per 08 §4.3.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why explicit, not Hangfire AutomaticRetry?</b> The deferred tier
/// fires the first attempt 30 dakika after the immediate-tier failure,
/// then escalates the inter-attempt delay (60 dk → 240 dk). Hangfire's
/// <see cref="AutomaticRetryAttribute"/> applies the same backoff schedule
/// to every job class, which would collide with the immediate tier's
/// (60s / 300s / 900s) schedule. Explicit re-scheduling keeps the two
/// tiers cleanly separated and lets each row's tier number land in the
/// log line.
/// </para>
/// <para>
/// <b>Exception classification mirrors the immediate tier:</b>
/// </para>
/// <list type="bullet">
///   <item><see cref="PermanentChannelDeliveryException"/> → FAILED +
///         admin alert (no further retry, no further tier).</item>
///   <item><see cref="TransientChannelDeliveryException"/> /
///         other <see cref="Exception"/>: while
///         <c>tier &lt; LastTier</c>, the row stays in DEFERRED and the
///         next tier is scheduled; at <see cref="LastTier"/> the row is
///         finalised as FAILED with the admin alert.</item>
/// </list>
/// </remarks>
[AutomaticRetry(Attempts = 0)] // We manage retries ourselves via tier scheduling.
public sealed class DeferredNotificationDeliveryJob
{
    public const int FirstTier = 1;
    public const int LastTier = 3;

    /// <summary>
    /// Delay applied between the deferred-tier attempt that just failed
    /// (1 → 2 / 2 → 3) and the next one. 08 §4.3 — 60 dakika then 4 saat.
    /// </summary>
    internal static readonly IReadOnlyDictionary<int, TimeSpan> NextTierDelay =
        new Dictionary<int, TimeSpan>
        {
            [1] = TimeSpan.FromMinutes(60),
            [2] = TimeSpan.FromHours(4),
        };

    private readonly AppDbContext _dbContext;
    private readonly IEnumerable<INotificationChannelHandler> _channelHandlers;
    private readonly INotificationAdminAlertSink _alertSink;
    private readonly IBackgroundJobScheduler _jobScheduler;
    private readonly ILogger<DeferredNotificationDeliveryJob> _logger;

    public DeferredNotificationDeliveryJob(
        AppDbContext dbContext,
        IEnumerable<INotificationChannelHandler> channelHandlers,
        INotificationAdminAlertSink alertSink,
        IBackgroundJobScheduler jobScheduler,
        ILogger<DeferredNotificationDeliveryJob> logger)
    {
        _dbContext = dbContext;
        _channelHandlers = channelHandlers;
        _alertSink = alertSink;
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    /// <summary>
    /// Hangfire entry point. <paramref name="tier"/> is 1-indexed (first
    /// deferred attempt = 1) and clamped to [<see cref="FirstTier"/>,
    /// <see cref="LastTier"/>] defensively so a hand-crafted schedule
    /// cannot push us into an infinite tier loop.
    /// </summary>
    public void Execute(Guid deliveryId, int tier, PerformContext? context)
    {
        var cancellationToken = context?.CancellationToken?.ShutdownToken
                                ?? CancellationToken.None;
        RunAsync(deliveryId, tier, cancellationToken).GetAwaiter().GetResult();
    }

    public async Task RunAsync(
        Guid deliveryId,
        int tier,
        CancellationToken cancellationToken)
    {
        if (tier < FirstTier || tier > LastTier)
        {
            _logger.LogError(
                "DeferredNotificationDeliveryJob received invalid tier {Tier} for DeliveryId={DeliveryId}; aborting.",
                tier,
                deliveryId);
            return;
        }

        var delivery = await _dbContext.Set<NotificationDelivery>()
            .FirstOrDefaultAsync(d => d.Id == deliveryId, cancellationToken);

        if (delivery is null)
        {
            _logger.LogWarning(
                "Deferred delivery {DeliveryId} not found — job skipped.",
                deliveryId);
            return;
        }

        if (delivery.Status == DeliveryStatus.SENT)
        {
            // A concurrent immediate-tier retry (race after a service
            // restart) or manual admin replay landed the email already.
            return;
        }

        if (delivery.Status == DeliveryStatus.FAILED)
        {
            // Already finalised — possibly by a manual admin override.
            // Nothing to do.
            return;
        }

        var notification = await _dbContext.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == delivery.NotificationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Notification {delivery.NotificationId} referenced by deferred delivery {delivery.Id} is missing.");

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

            _logger.LogInformation(
                "Deferred delivery {DeliveryId} recovered on tier {Tier}.",
                delivery.Id,
                tier);
        }
        catch (PermanentChannelDeliveryException pex)
        {
            delivery.Status = DeliveryStatus.FAILED;
            delivery.LastError = Truncate(pex.Message, 1000);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                pex,
                "Deferred delivery {DeliveryId} permanently failed on tier {Tier} — alerting admins.",
                delivery.Id,
                tier);

            await _alertSink.RaiseDeliveryExhaustedAsync(delivery, cancellationToken);
        }
        catch (Exception ex)
        {
            if (tier >= LastTier)
            {
                // Last tier exhausted — finalise as FAILED + admin alert.
                delivery.Status = DeliveryStatus.FAILED;
                delivery.LastError = Truncate(ex.Message, 1000);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(
                    ex,
                    "Deferred delivery {DeliveryId} exhausted final tier — alerting admins.",
                    delivery.Id);

                await _alertSink.RaiseDeliveryExhaustedAsync(delivery, cancellationToken);
                return;
            }

            // Keep the row in DEFERRED, refresh LastError, schedule the next tier.
            delivery.Status = DeliveryStatus.DEFERRED;
            delivery.LastError = Truncate(ex.Message, 1000);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var nextTier = tier + 1;
            var delay = NextTierDelay[tier];

            Expression<Action<DeferredNotificationDeliveryJob>> call =
                job => job.Execute(delivery.Id, nextTier, null!);

            var jobId = _jobScheduler.Schedule(call, delay);

            _logger.LogWarning(
                ex,
                "Deferred delivery {DeliveryId} failed on tier {Tier} — next tier {NextTier} in {DelayMinutes} min (jobId={JobId}).",
                delivery.Id,
                tier,
                nextTier,
                (int)delay.TotalMinutes,
                jobId);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
