namespace Skinora.Notifications.Application.Notifications;

/// <summary>
/// Module-facing entry point invoked by domain event handlers (T44+) to fan a
/// single <see cref="NotificationRequest"/> out across the platform-in-app
/// channel and any enabled external channels (05 §7.1).
/// </summary>
/// <remarks>
/// <para>
/// The dispatcher writes <see cref="Domain.Entities.Notification"/> and one
/// <see cref="Domain.Entities.NotificationDelivery"/> per enabled external
/// channel to <c>AppDbContext</c> but does NOT call <c>SaveChangesAsync</c> —
/// the caller (typically an Outbox-driven MediatR notification handler)
/// owns the surrounding unit of work so the writes commit atomically with
/// the source <c>OutboxMessage</c> status update (05 §5.1, 09 §9.3).
/// </para>
/// <para>
/// External channel HTTP work runs out-of-band as
/// <see cref="DeliveryJobs.NotificationDeliveryJob"/> Hangfire jobs that the
/// dispatcher enqueues right before returning. Job handlers re-load the
/// delivery row at runtime so an aborted producer transaction (no commit →
/// no row → no-op job) does not produce phantom sends (09 §13.3).
/// </para>
/// </remarks>
public interface INotificationDispatcher
{
    Task DispatchAsync(NotificationRequest request, CancellationToken cancellationToken);
}
