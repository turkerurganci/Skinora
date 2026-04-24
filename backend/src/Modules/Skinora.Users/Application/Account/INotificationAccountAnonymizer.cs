namespace Skinora.Users.Application.Account;

/// <summary>
/// Ports the notification-side cleanup required by 06 §6.2 when an account is
/// permanently deleted:
/// <list type="bullet">
///   <item><description><c>UserNotificationPreference</c> rows are soft-deleted
///   and <c>ExternalId</c> is cleared (email, Telegram chat ID, Discord user
///   ID are PII).</description></item>
///   <item><description><c>NotificationDelivery.TargetExternalId</c> is rewritten
///   into a channel-specific masked form (<c>***@***.com</c>,
///   <c>tg:***{last4}</c>, <c>dsc:***{last4}</c>) so the delivery audit trail
///   survives without leaking the subscriber address.</description></item>
/// </list>
/// Implementation lives in <c>Skinora.Notifications</c> because that module
/// owns the entities; the interface lives in <c>Skinora.Users</c> so
/// <c>AccountLifecycleService</c> stays off the Notifications reference graph
/// (mirrors the T35 <c>INotificationPreferenceStore</c> split).
/// </summary>
public interface INotificationAccountAnonymizer
{
    /// <summary>
    /// Anonymizes every notification-side record for <paramref name="userId"/>.
    /// Returns the number of preference rows soft-deleted + delivery rows
    /// masked — used by callers for telemetry/assertions but not surfaced in
    /// the HTTP response.
    /// </summary>
    Task<NotificationAnonymizationResult> AnonymizeAsync(
        Guid userId, CancellationToken cancellationToken);
}

public sealed record NotificationAnonymizationResult(
    int PreferencesSoftDeleted,
    int DeliveriesMasked);
