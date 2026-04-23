namespace Skinora.Users.Application.Settings;

/// <summary>
/// Drives Telegram bot linking (07 §5.11, §5.11b, §5.14). The connect flow
/// issues a short-lived <c>SKN-XXXXXX</c> code that the user pastes into the
/// Telegram bot via <c>/start {code}</c>; the webhook then consumes the code
/// and creates/updates the <c>UserNotificationPreference</c> row for the
/// channel. Real bot push delivery + secret header validation is wired into
/// T79 — this service exposes the interface the real implementation reuses.
/// </summary>
public interface ITelegramConnectionService
{
    Task<TelegramConnectResult> InitiateAsync(
        Guid userId, CancellationToken cancellationToken);

    Task<TelegramWebhookResult> ProcessWebhookAsync(
        TelegramWebhookPayload payload, CancellationToken cancellationToken);

    Task<TelegramDisconnectResult> DisconnectAsync(
        Guid userId, CancellationToken cancellationToken);
}

public sealed record TelegramConnectResult(string Code, string BotUrl, TimeSpan Ttl);

public sealed record TelegramWebhookPayload(string? Code, string? TelegramUsername, long? TelegramUserId);

public enum TelegramWebhookStatus
{
    Linked,
    InvalidOrExpiredCode,
    AlreadyLinkedToAnotherUser,
    Ignored,
}

public sealed record TelegramWebhookResult(TelegramWebhookStatus Status, Guid? LinkedUserId);

public enum TelegramDisconnectStatus
{
    Removed,
    NotConnected,
    UserNotFound,
}

public sealed record TelegramDisconnectResult(TelegramDisconnectStatus Status);
