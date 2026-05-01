namespace Skinora.Notifications.Application.Inbox;

/// <summary>
/// Stable error codes for 07 §8.1–§8.4. Mirrors the convention used by
/// <c>Auth*ErrorCodes</c> / <c>SettingsErrorCodes</c> so the
/// <see cref="Skinora.Shared.Models.ApiResponse{T}"/> envelope carries
/// human-readable codes rather than ad-hoc strings.
/// </summary>
public static class NotificationInboxErrorCodes
{
    /// <summary>404 — id resolves to no row.</summary>
    public const string NotificationNotFound = "NOTIFICATION_NOT_FOUND";

    /// <summary>403 — row exists but belongs to another user.</summary>
    public const string Forbidden = "FORBIDDEN";
}
