namespace Skinora.Notifications.Application.Inbox;

/// <summary>Body of 07 §8.2 — <c>GET /notifications/unread-count</c>.</summary>
public sealed record UnreadCountResponse(int UnreadCount);

/// <summary>Body of 07 §8.3 — <c>POST /notifications/mark-all-read</c>.</summary>
public sealed record MarkAllReadResponse(int MarkedCount);

/// <summary>Discriminated outcome for 07 §8.4 — <c>PUT /notifications/:id/read</c>.</summary>
public enum MarkReadOutcome
{
    /// <summary>Row exists, is owned by the caller, transitioned to read (or was already read).</summary>
    Success,

    /// <summary>No notification with the given id (404 NOTIFICATION_NOT_FOUND).</summary>
    NotFound,

    /// <summary>Notification belongs to another user (403 FORBIDDEN).</summary>
    Forbidden,
}
