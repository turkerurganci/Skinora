namespace Skinora.Shared.Telegram;

/// <summary>
/// Root exception type for Telegram Bot API transport failures. Callers
/// should never catch this directly — branch on the
/// <see cref="TelegramTransientException"/>,
/// <see cref="TelegramPermanentException"/> or
/// <see cref="TelegramForbiddenException"/> subclasses so retry +
/// preference-disable decisions stay correct (08 §5.4).
/// </summary>
public abstract class TelegramBotException : Exception
{
    public int? HttpStatusCode { get; }
    public int? TelegramErrorCode { get; }
    public string? TelegramErrorDescription { get; }

    protected TelegramBotException(
        string message,
        int? httpStatusCode,
        int? telegramErrorCode,
        string? telegramErrorDescription,
        Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
        TelegramErrorCode = telegramErrorCode;
        TelegramErrorDescription = telegramErrorDescription;
    }
}

/// <summary>
/// Transient failure — Telegram returned 429 (with <c>retry_after</c>),
/// 5xx, or the call failed at the transport layer (network, DNS,
/// timeout). The notification delivery pipeline retries on the
/// immediate-tier backoff and then escalates to the deferred-tier job
/// (08 §5.4).
/// </summary>
public sealed class TelegramTransientException : TelegramBotException
{
    /// <summary>
    /// <c>retry_after</c> value (seconds) when Telegram returned 429.
    /// Null for transport / 5xx failures.
    /// </summary>
    public int? RetryAfterSeconds { get; }

    public TelegramTransientException(
        string message,
        int? httpStatusCode = null,
        int? telegramErrorCode = null,
        string? telegramErrorDescription = null,
        int? retryAfterSeconds = null,
        Exception? innerException = null)
        : base(message, httpStatusCode, telegramErrorCode, telegramErrorDescription, innerException)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}

/// <summary>
/// Permanent failure — Telegram returned 400 (bad request, e.g. invalid
/// chat id) or any other 4xx that retrying cannot resolve. The delivery
/// row is flipped straight to <c>FAILED</c> with an admin alert.
/// </summary>
public sealed class TelegramPermanentException : TelegramBotException
{
    public TelegramPermanentException(
        string message,
        int? httpStatusCode = null,
        int? telegramErrorCode = null,
        string? telegramErrorDescription = null,
        Exception? innerException = null)
        : base(message, httpStatusCode, telegramErrorCode, telegramErrorDescription, innerException)
    {
    }
}

/// <summary>
/// 403 Forbidden — the bot can no longer reach the user (08 §5.4).
/// <see cref="Reason"/> captures the parsed cause so callers can
/// auto-disable the preference and surface the right user-facing
/// message.
/// </summary>
public sealed class TelegramForbiddenException : TelegramBotException
{
    public TelegramForbiddenReason Reason { get; }

    public TelegramForbiddenException(
        TelegramForbiddenReason reason,
        string message,
        int? telegramErrorCode = null,
        string? telegramErrorDescription = null,
        Exception? innerException = null)
        : base(message, 403, telegramErrorCode, telegramErrorDescription, innerException)
    {
        Reason = reason;
    }
}

/// <summary>
/// 08 §5.4 — Telegram's 403 <c>error_description</c> taxonomy. The
/// channel handler maps each value to the right side-effect (preference
/// disable + reconnect notice / admin alert / preference + warning).
/// </summary>
public enum TelegramForbiddenReason
{
    /// <summary>User blocked the bot.</summary>
    BotBlockedByUser,

    /// <summary>Telegram account deactivated / deleted.</summary>
    UserDeactivated,

    /// <summary>Bot tried to message another bot (data inconsistency).</summary>
    CannotMessageBots,

    /// <summary>User never <c>/start</c>'d or cleared chat history.</summary>
    CannotInitiateConversation,

    /// <summary>403 with an unrecognized description — treat as warning + admin alert.</summary>
    Unknown,
}
