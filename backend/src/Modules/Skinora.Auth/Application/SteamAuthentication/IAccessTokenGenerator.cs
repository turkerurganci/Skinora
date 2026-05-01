using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Issues short-lived JWT access tokens signed with the current JWT
/// secret — 05 §6.1 (15 min default). Role + permission claims are resolved
/// from <see cref="IAdminAuthorityResolver"/> at issuance time so a refresh
/// always reflects the live <c>AdminUserRole → AdminRole</c> assignments.
/// </summary>
public interface IAccessTokenGenerator
{
    Task<GeneratedAccessToken> GenerateAsync(
        User user, CancellationToken cancellationToken);
}

public sealed record GeneratedAccessToken(string Token, DateTime ExpiresAt);
