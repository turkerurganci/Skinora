using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Configuration;
using Skinora.Auth.Domain.Entities;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Application.Session;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly AppDbContext _db;
    private readonly IRefreshTokenCache _cache;
    private readonly IAccessTokenGenerator _accessTokenGenerator;
    private readonly IRefreshTokenGenerator _refreshTokenGenerator;
    private readonly JwtSettings _settings;

    public RefreshTokenService(
        AppDbContext db,
        IRefreshTokenCache cache,
        IAccessTokenGenerator accessTokenGenerator,
        IRefreshTokenGenerator refreshTokenGenerator,
        IOptions<JwtSettings> settings)
    {
        _db = db;
        _cache = cache;
        _accessTokenGenerator = accessTokenGenerator;
        _refreshTokenGenerator = refreshTokenGenerator;
        _settings = settings.Value;
    }

    public async Task<RotateOutcome> RotateAsync(
        string plainTextToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(plainTextToken))
            return new RotateOutcome.Missing();

        var hash = HashToken(plainTextToken);
        var existing = await _db.Set<RefreshToken>()
            .FirstOrDefaultAsync(t => t.Token == hash, cancellationToken);

        if (existing is null)
        {
            await _cache.RemoveAsync(hash, cancellationToken);
            return new RotateOutcome.Invalid();
        }

        // Reuse detection — a token that is already revoked OR already swapped
        // for a successor is a compromise signal (05 §6.1). Wipe every active
        // refresh token for the user so any clone is also killed.
        if (existing.IsRevoked || existing.ReplacedByTokenId is not null)
        {
            await _cache.RemoveAsync(hash, cancellationToken);
            await MassRevokeAsync(existing.UserId, cancellationToken);
            return new RotateOutcome.Reused(existing.UserId);
        }

        if (existing.ExpiresAt <= DateTime.UtcNow)
        {
            await _cache.RemoveAsync(hash, cancellationToken);
            return new RotateOutcome.Expired();
        }

        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(u => u.Id == existing.UserId, cancellationToken);
        if (user is null)
        {
            await _cache.RemoveAsync(hash, cancellationToken);
            return new RotateOutcome.Invalid();
        }

        // Rotation — issue the successor first, then atomically mark the old
        // token revoked with a ReplacedByTokenId pointer. RefreshTokenGenerator
        // commits its own insert, so we SaveChangesAsync a second time for the
        // old-token update.
        var newRefresh = await _refreshTokenGenerator.IssueAsync(
            existing.UserId, ipAddress, userAgent, cancellationToken);

        existing.IsRevoked = true;
        existing.RevokedAt = DateTime.UtcNow;
        existing.ReplacedByTokenId = newRefresh.Entity.Id;
        await _db.SaveChangesAsync(cancellationToken);

        await _cache.RemoveAsync(hash, cancellationToken);
        var newHash = newRefresh.Entity.Token;
        var newEntry = new RefreshTokenCacheEntry(
            newRefresh.Entity.Id, existing.UserId, newRefresh.ExpiresAt);
        await _cache.SetAsync(
            newHash, newEntry, newRefresh.ExpiresAt - DateTime.UtcNow, cancellationToken);

        var access = await _accessTokenGenerator.GenerateAsync(user, cancellationToken);
        return new RotateOutcome.Success(user, access, newRefresh);
    }

    public async Task<bool> RevokeAsync(string plainTextToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(plainTextToken)) return false;

        var hash = HashToken(plainTextToken);
        var token = await _db.Set<RefreshToken>()
            .FirstOrDefaultAsync(t => t.Token == hash, cancellationToken);

        // Always invalidate the cache entry regardless of DB state.
        await _cache.RemoveAsync(hash, cancellationToken);

        if (token is null || token.IsRevoked) return false;

        token.IsRevoked = true;
        token.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task MassRevokeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var active = await _db.Set<RefreshToken>()
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var token in active)
        {
            token.IsRevoked = true;
            token.RevokedAt = now;
            await _cache.RemoveAsync(token.Token, cancellationToken);
        }

        if (active.Count > 0) await _db.SaveChangesAsync(cancellationToken);
    }

    private static string HashToken(string plainText)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainText)));

    /// <summary>
    /// Exposed so the token generator and cache seeding can use the same hash
    /// encoding (05 §6.1 "cache keyed by token hash"). Internal to the Auth
    /// assembly — controllers never see plaintext in storage form.
    /// </summary>
    internal static string ComputeHash(string plainText) => HashToken(plainText);
}
