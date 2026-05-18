namespace Skinora.Shared.Discord;

/// <summary>
/// Root exception type for Discord bot API transport failures. Callers
/// should never catch this directly — branch on the
/// <see cref="DiscordTransientException"/>,
/// <see cref="DiscordPermanentException"/>,
/// <see cref="DiscordForbiddenException"/> or
/// <see cref="DiscordUnauthorizedException"/> subclasses so retry,
/// preference-disable and admin-alert decisions stay correct
/// (08 §6.4).
/// </summary>
public abstract class DiscordBotException : Exception
{
    public int? HttpStatusCode { get; }
    public int? DiscordErrorCode { get; }
    public string? DiscordErrorMessage { get; }

    protected DiscordBotException(
        string message,
        int? httpStatusCode,
        int? discordErrorCode,
        string? discordErrorMessage,
        Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
        DiscordErrorCode = discordErrorCode;
        DiscordErrorMessage = discordErrorMessage;
    }
}

/// <summary>
/// Transient failure — Discord returned 429 (with <c>retry_after</c>),
/// 5xx, or the call failed at the transport layer (network, DNS,
/// timeout). The notification delivery pipeline retries on the
/// immediate-tier backoff and then escalates to the deferred-tier job
/// (08 §6.4 — "5xx → 3 deneme (1dk, 5dk, 15dk)").
/// </summary>
public sealed class DiscordTransientException : DiscordBotException
{
    /// <summary>
    /// <c>retry_after</c> value (seconds) when Discord returned 429.
    /// Null for transport / 5xx failures. The bucket the request was
    /// in (<see cref="Bucket"/>) is also captured so the rate limiter
    /// can pause the right shard.
    /// </summary>
    public double? RetryAfterSeconds { get; }

    /// <summary>
    /// Discord rate-limit bucket identifier
    /// (<c>X-RateLimit-Bucket</c>) — null when the failure was not a
    /// 429 or when Discord did not return the header.
    /// </summary>
    public string? Bucket { get; }

    /// <summary>
    /// <c>true</c> when the 429 response carried <c>"global": true</c>
    /// — the rate limiter pauses the global window rather than the
    /// per-bucket gate.
    /// </summary>
    public bool IsGlobal { get; }

    public DiscordTransientException(
        string message,
        int? httpStatusCode = null,
        int? discordErrorCode = null,
        string? discordErrorMessage = null,
        double? retryAfterSeconds = null,
        string? bucket = null,
        bool isGlobal = false,
        Exception? innerException = null)
        : base(message, httpStatusCode, discordErrorCode, discordErrorMessage, innerException)
    {
        RetryAfterSeconds = retryAfterSeconds;
        Bucket = bucket;
        IsGlobal = isGlobal;
    }
}

/// <summary>
/// Permanent failure — Discord returned 400 (validation), 404 (channel
/// or user not found) or any other 4xx that retrying cannot resolve.
/// The delivery row is flipped to <c>FAILED</c> with an admin alert.
/// 404 specifically (08 §6.4 "Kullanıcı bulunamadı") also disables the
/// preference so future jobs don't keep firing into the void.
/// </summary>
public sealed class DiscordPermanentException : DiscordBotException
{
    public DiscordPermanentException(
        string message,
        int? httpStatusCode = null,
        int? discordErrorCode = null,
        string? discordErrorMessage = null,
        Exception? innerException = null)
        : base(message, httpStatusCode, discordErrorCode, discordErrorMessage, innerException)
    {
    }
}

/// <summary>
/// 401 Unauthorized — bot token invalid or revoked (08 §6.4 "Bot token
/// geçersiz / expired"). The channel handler escalates to an admin
/// alert and pauses the DM queue so a single token rotation doesn't
/// burn the whole delivery backlog as failures.
/// </summary>
public sealed class DiscordUnauthorizedException : DiscordBotException
{
    public DiscordUnauthorizedException(
        string message,
        int? discordErrorCode = null,
        string? discordErrorMessage = null,
        Exception? innerException = null)
        : base(message, 401, discordErrorCode, discordErrorMessage, innerException)
    {
    }
}

/// <summary>
/// 403 Forbidden — the bot can no longer reach the user (08 §6.4).
/// <see cref="Reason"/> captures the parsed cause so callers can
/// auto-disable the preference and surface the right user-facing
/// message.
/// </summary>
public sealed class DiscordForbiddenException : DiscordBotException
{
    public DiscordForbiddenReason Reason { get; }

    public DiscordForbiddenException(
        DiscordForbiddenReason reason,
        string message,
        int? discordErrorCode = null,
        string? discordErrorMessage = null,
        Exception? innerException = null)
        : base(message, 403, discordErrorCode, discordErrorMessage, innerException)
    {
        Reason = reason;
    }
}

/// <summary>
/// 08 §6.4 — Discord's 403 taxonomy. The channel handler maps each
/// value to the right side-effect (preference disable + reconnect
/// guidance vs guild-join nudge).
/// </summary>
public enum DiscordForbiddenReason
{
    /// <summary>
    /// CreateDM returned 403 — bot and user have no mutual guild and
    /// the application isn't user-installed (08 §6.4 row 2). Surface
    /// the "Skinora Discord sunucusuna katılın" message via the
    /// fallback channel.
    /// </summary>
    MutualGuildRequired,

    /// <summary>
    /// SendMessage returned 403 — DM channel exists but the user has
    /// closed DMs from server members (08 §6.4 row 1). Disable the
    /// preference and surface the "DM ayarlarınızı açın" message.
    /// </summary>
    DmClosed,

    /// <summary>
    /// 403 with an unrecognized Discord error code — treat as warning
    /// + admin alert.
    /// </summary>
    Unknown,
}

/// <summary>
/// OAuth2 token-exchange failure surface. Thrown by
/// <see cref="IDiscordOAuthClient"/> for failures the connection
/// service must distinguish from the happy path
/// (<c>access_denied</c>, <c>invalid_grant</c>, transport errors,
/// 5xx). Connection service maps each value to the documented
/// callback redirect (08 §6.4 OAuth2 hata tablosu).
/// </summary>
public sealed class DiscordOAuthExchangeException : Exception
{
    public DiscordOAuthFailureReason Reason { get; }
    public int? HttpStatusCode { get; }

    public DiscordOAuthExchangeException(
        DiscordOAuthFailureReason reason,
        string message,
        int? httpStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
        HttpStatusCode = httpStatusCode;
    }
}

public enum DiscordOAuthFailureReason
{
    /// <summary>
    /// Discord returned a 4xx with <c>invalid_grant</c> — the
    /// authorization code expired, was already used or was tampered
    /// with. Redirect: <c>?discord=error&amp;reason=expired</c>.
    /// </summary>
    InvalidGrant,

    /// <summary>
    /// Token exchange failed for a non-4xx reason — Discord returned
    /// 5xx, the call timed out, or the network failed. Redirect:
    /// <c>?discord=error&amp;reason=exchange_failed</c>.
    /// </summary>
    TokenExchangeFailed,

    /// <summary>
    /// <c>GET /users/@me</c> failed after a successful token exchange.
    /// The access_token is dropped (never persisted) and the user is
    /// surfaced the generic "geçici hata" redirect.
    /// </summary>
    UsersMeFailed,
}
