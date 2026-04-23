using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Application.Session;

/// <summary>
/// Refresh token lifecycle: rotation on <c>/auth/refresh</c>, revocation on
/// <c>/auth/logout</c>, and mass-revocation when a rotated token is reused
/// (05 §6.1, 07 §4.9–§4.10).
/// </summary>
public interface IRefreshTokenService
{
    Task<RotateOutcome> RotateAsync(
        string plainTextToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken);

    Task<bool> RevokeAsync(string plainTextToken, CancellationToken cancellationToken);
}

/// <summary>Outcome of a <see cref="IRefreshTokenService.RotateAsync"/> call.</summary>
public abstract record RotateOutcome
{
    /// <summary>Token valid — new access + refresh pair issued.</summary>
    public sealed record Success(
        User User, GeneratedAccessToken Access, GeneratedRefreshToken Refresh) : RotateOutcome;

    /// <summary>No refresh cookie on the request — <c>REFRESH_TOKEN_MISSING</c>.</summary>
    public sealed record Missing : RotateOutcome;

    /// <summary>Token not found in DB or soft-deleted — <c>REFRESH_TOKEN_INVALID</c>.</summary>
    public sealed record Invalid : RotateOutcome;

    /// <summary>Token past its <c>ExpiresAt</c> — <c>REFRESH_TOKEN_EXPIRED</c>.</summary>
    public sealed record Expired : RotateOutcome;

    /// <summary>
    /// Token was already rotated or revoked — treated as a compromise signal
    /// and triggers mass-revocation of all active refresh tokens for the user
    /// (05 §6.1, OWASP refresh token reuse detection).
    /// </summary>
    public sealed record Reused(Guid UserId) : RotateOutcome;
}
