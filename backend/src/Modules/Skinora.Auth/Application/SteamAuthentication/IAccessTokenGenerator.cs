using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Issues short-lived JWT access tokens signed with the current JWT
/// secret — 05 §6.1 (15 min default).
/// </summary>
public interface IAccessTokenGenerator
{
    GeneratedAccessToken Generate(User user);
}

public sealed record GeneratedAccessToken(string Token, DateTime ExpiresAt);
