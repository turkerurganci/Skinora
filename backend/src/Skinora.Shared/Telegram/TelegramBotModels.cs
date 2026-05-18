namespace Skinora.Shared.Telegram;

/// <summary>
/// Request DTO for <c>sendMessage</c>. <paramref name="ChatId"/> is the
/// Telegram user/chat id as a string (Telegram accepts both numeric
/// chat id and <c>@channel</c> form; the integer id is what the
/// connection webhook stores, see 07 §5.11b).
/// </summary>
public sealed record TelegramSendMessageRequest(
    string ChatId,
    string Text,
    bool DisableNotification = false);

public sealed record TelegramSendMessageResult(long MessageId);

/// <summary>
/// Request DTO for <c>setWebhook</c> (08 §5.2). <paramref name="AllowedUpdates"/>
/// defaults to <c>["message"]</c> at the client level; pass <c>null</c>
/// to accept the documented default.
/// </summary>
public sealed record TelegramSetWebhookRequest(
    string Url,
    string SecretToken,
    int MaxConnections = 40,
    IReadOnlyCollection<string>? AllowedUpdates = null,
    bool DropPendingUpdates = false);
