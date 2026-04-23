namespace Skinora.Users.Application.Settings;

/// <summary>
/// Applies <c>PUT /users/me/settings/notifications</c> (07 §5.9). Only the
/// channels present in the request are touched; each channel must already be
/// connected before it can be toggled — unconnected channels return
/// <c>CHANNEL_NOT_CONNECTED</c>.
/// </summary>
public interface INotificationPreferenceService
{
    Task<NotificationPreferenceUpdateResult> UpdateAsync(
        Guid userId,
        UpdateNotificationsRequest request,
        CancellationToken cancellationToken);
}

public enum NotificationPreferenceUpdateStatus
{
    Success,
    UserNotFound,
    ChannelNotConnected,
    ValidationError,
}

public sealed record NotificationPreferenceUpdateResult(
    NotificationPreferenceUpdateStatus Status,
    string? FailedChannel)
{
    public static NotificationPreferenceUpdateResult Success()
        => new(NotificationPreferenceUpdateStatus.Success, null);

    public static NotificationPreferenceUpdateResult Failure(
        NotificationPreferenceUpdateStatus status, string? failedChannel = null)
        => new(status, failedChannel);
}
