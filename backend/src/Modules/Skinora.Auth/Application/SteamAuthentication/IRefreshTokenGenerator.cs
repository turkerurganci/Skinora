using Skinora.Auth.Domain.Entities;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Issues and persists a new refresh token for a just-authenticated user —
/// 05 §6.1, 06 §3.3.
/// </summary>
public interface IRefreshTokenGenerator
{
    Task<GeneratedRefreshToken> IssueAsync(
        Guid userId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken);
}

public sealed record GeneratedRefreshToken(RefreshToken Entity, string PlainTextToken, DateTime ExpiresAt);
