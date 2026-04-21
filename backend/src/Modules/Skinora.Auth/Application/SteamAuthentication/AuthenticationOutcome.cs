using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Discriminated outcome of the Steam OpenID login pipeline — mapped by
/// the controller to <c>?status=...</c> or <c>?error=...</c> per 07 §4.3.
/// </summary>
public abstract record AuthenticationOutcome
{
    private AuthenticationOutcome() { }

    public sealed record Success(
        User User,
        GeneratedAccessToken AccessToken,
        GeneratedRefreshToken RefreshToken,
        bool IsNewUser) : AuthenticationOutcome;

    public sealed record AuthFailed(string Reason) : AuthenticationOutcome;

    public sealed record AccountBanned(Guid UserId) : AuthenticationOutcome;

    public sealed record GeoBlocked(string? CountryCode) : AuthenticationOutcome;

    public sealed record SanctionsMatch(string? List) : AuthenticationOutcome;
}
