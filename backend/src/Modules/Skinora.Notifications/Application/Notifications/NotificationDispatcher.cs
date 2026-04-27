using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Domain.Entities;
using Skinora.Notifications.Infrastructure.DeliveryJobs;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Notifications.Application.Notifications;

/// <summary>
/// Default <see cref="INotificationDispatcher"/> implementing the fan-out
/// pipeline described in 05 §7.1.
/// </summary>
/// <remarks>
/// <para>
/// Steps performed in order:
/// </para>
/// <list type="number">
///   <item>Resolve the user's <see cref="User.PreferredLanguage"/> (defaults
///         to <c>en</c> when missing).</item>
///   <item>Render the <see cref="NotificationType"/> template via
///         <see cref="INotificationTemplateResolver"/>.</item>
///   <item>Add the platform-in-app <see cref="Notification"/> row (always
///         on, 05 §7.4).</item>
///   <item>For every enabled <see cref="UserNotificationPreference"/> with
///         a non-empty <see cref="UserNotificationPreference.ExternalId"/>,
///         add a <see cref="NotificationDelivery"/> row in
///         <see cref="DeliveryStatus.PENDING"/>.</item>
///   <item>Enqueue one <see cref="NotificationDeliveryJob"/> per delivery
///         row.</item>
/// </list>
/// <para>
/// The dispatcher does NOT call <c>SaveChangesAsync</c> — the surrounding
/// MediatR notification handler runs inside the
/// <see cref="Skinora.Shared.Outbox"/> dispatcher's batch
/// <c>SaveChangesAsync</c>, so all writes commit atomically with the
/// originating <c>OutboxMessage</c> status update (05 §5.1, 09 §9.3).
/// </para>
/// <para>
/// Hangfire enqueues happen pre-commit. The job re-loads the delivery row at
/// runtime and no-ops when it is missing, so an aborted producer transaction
/// never produces phantom external sends (09 §13.3 atomicity boundary).
/// </para>
/// </remarks>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private const string DefaultLocale = "en";

    private readonly AppDbContext _dbContext;
    private readonly INotificationTemplateResolver _templateResolver;
    private readonly IBackgroundJobScheduler _jobScheduler;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        AppDbContext dbContext,
        INotificationTemplateResolver templateResolver,
        IBackgroundJobScheduler jobScheduler,
        ILogger<NotificationDispatcher> logger)
    {
        _dbContext = dbContext;
        _templateResolver = templateResolver;
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    public async Task DispatchAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        var locale = await ResolveLocaleAsync(request.UserId, cancellationToken);
        var rendered = _templateResolver.Resolve(request.Type, locale, request.Parameters);

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            TransactionId = request.TransactionId,
            Type = request.Type,
            Title = rendered.Title,
            Body = rendered.Body,
            IsRead = false,
        };

        _dbContext.Set<Notification>().Add(notification);

        var enabledPreferences = await _dbContext.Set<UserNotificationPreference>()
            .Where(p => p.UserId == request.UserId
                        && p.IsEnabled
                        && p.ExternalId != null
                        && p.ExternalId != string.Empty)
            .ToListAsync(cancellationToken);

        var enqueuedDeliveryIds = new List<Guid>(enabledPreferences.Count);

        foreach (var preference in enabledPreferences)
        {
            var delivery = new NotificationDelivery
            {
                Id = Guid.NewGuid(),
                NotificationId = notification.Id,
                Channel = preference.Channel,
                TargetExternalId = preference.ExternalId!,
                Status = DeliveryStatus.PENDING,
                AttemptCount = 0,
            };

            _dbContext.Set<NotificationDelivery>().Add(delivery);
            enqueuedDeliveryIds.Add(delivery.Id);
        }

        // Hangfire enqueue must happen after the rows are tracked so each job
        // carries the persisted DeliveryId. The job re-loads the row at run
        // time and no-ops when the producer transaction never committed (09
        // §13.3).
        foreach (var deliveryId in enqueuedDeliveryIds)
        {
            Expression<Action<NotificationDeliveryJob>> call =
                job => job.Execute(deliveryId, null!);

            _jobScheduler.Enqueue<NotificationDeliveryJob>(call);
        }

        _logger.LogInformation(
            "Notification dispatched — UserId={UserId} Type={Type} Locale={Locale} Channels={ChannelCount}",
            request.UserId,
            request.Type,
            locale,
            enabledPreferences.Count);
    }

    private async Task<string> ResolveLocaleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var locale = await _dbContext.Set<User>()
            .Where(u => u.Id == userId)
            .Select(u => u.PreferredLanguage)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(locale) ? DefaultLocale : locale;
    }
}
