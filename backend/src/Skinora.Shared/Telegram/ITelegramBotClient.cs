namespace Skinora.Shared.Telegram;

/// <summary>
/// Low-level HTTP transport for the Telegram Bot API
/// (<c>sendMessage</c>, <c>setWebhook</c>) — 08 §5.2. Independent of who
/// is calling it; the notification dispatcher (notifications module),
/// the bot management runbook and integration tests all consume the
/// same interface so auth, retry classification and Telegram error
/// mapping live in one place.
/// </summary>
/// <remarks>
/// <para>
/// Throws either <see cref="TelegramTransientException"/> (5xx, 429,
/// network / transport) or <see cref="TelegramPermanentException"/> /
/// <see cref="TelegramForbiddenException"/> (400 / 403 / other 4xx).
/// Successful sends return <see cref="TelegramSendMessageResult"/>
/// (Telegram <c>message_id</c> for downstream correlation).
/// </para>
/// </remarks>
public interface ITelegramBotClient
{
    Task<TelegramSendMessageResult> SendMessageAsync(
        TelegramSendMessageRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Registers / updates the webhook (08 §5.2). Called by the setup
    /// runbook / CLI; the runtime path never invokes it directly.
    /// </summary>
    Task SetWebhookAsync(
        TelegramSetWebhookRequest request,
        CancellationToken cancellationToken);
}
