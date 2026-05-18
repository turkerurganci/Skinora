namespace Skinora.Users.Application.Settings;

/// <summary>
/// Drives Discord OAuth linking (07 §5.12, §5.13, §5.15).
/// </summary>
public interface IDiscordConnectionService
{
    /// <summary>
    /// Returns the outgoing authorize URL the frontend must redirect the user
    /// to. The state parameter is persisted in the OAuth state store and
    /// consumed in the callback to establish the session binding without
    /// needing the refresh token cookie on <c>/discord/callback</c>
    /// (07 §5.13 — path outside the refresh cookie's scope).
    /// </summary>
    Task<DiscordAuthorizeUrl> BuildAuthorizeUrlAsync(
        Guid userId, CancellationToken cancellationToken);

    Task<DiscordCallbackResult> HandleCallbackAsync(
        string? code, string? state, string? error, CancellationToken cancellationToken);

    Task<DiscordDisconnectResult> DisconnectAsync(
        Guid userId, CancellationToken cancellationToken);
}

public sealed record DiscordAuthorizeUrl(string Url);

public enum DiscordCallbackStatus
{
    Connected,
    UserDenied,
    InvalidState,
    AlreadyLinkedToAnotherUser,
    ExchangeFailed,
    /// <summary>
    /// Discord returned <c>invalid_grant</c> on the token endpoint —
    /// the authorization code expired or was already used. T80 added
    /// this distinction so the callback redirect can surface the
    /// documented <c>?reason=expired</c> instead of the generic
    /// failure path (08 §6.4 OAuth2 hata tablosu).
    /// </summary>
    InvalidGrant,
}

public sealed record DiscordCallbackResult(
    DiscordCallbackStatus Status,
    Guid? UserId,
    string? Username);

public enum DiscordDisconnectStatus
{
    Removed,
    NotConnected,
    UserNotFound,
}

public sealed record DiscordDisconnectResult(DiscordDisconnectStatus Status);
