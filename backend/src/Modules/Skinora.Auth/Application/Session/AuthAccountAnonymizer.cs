using Microsoft.EntityFrameworkCore;
using Skinora.Auth.Domain.Entities;
using Skinora.Shared.Persistence;
using Skinora.Users.Application.Account;

namespace Skinora.Auth.Application.Session;

/// <summary>
/// <see cref="IAuthAccountAnonymizer"/> impl — 06 §6.2 session-side cleanup
/// for deactivate + delete. Placed in <c>Skinora.Auth</c> because it owns
/// <see cref="RefreshToken"/>.
/// </summary>
public sealed class AuthAccountAnonymizer : IAuthAccountAnonymizer
{
    private readonly AppDbContext _db;
    private readonly IRefreshTokenCache _cache;
    private readonly TimeProvider _clock;

    public AuthAccountAnonymizer(
        AppDbContext db,
        IRefreshTokenCache cache,
        TimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<int> RevokeAllSessionsAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var tokens = await _db.Set<RefreshToken>()
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0) return 0;

        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var token in tokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = now;
            await _cache.RemoveAsync(token.Token, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return tokens.Count;
    }

    public async Task<int> AnonymizeSessionsAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var tokens = await _db.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Where(t => t.UserId == userId)
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0) return 0;

        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var token in tokens)
        {
            if (!token.IsRevoked)
            {
                token.IsRevoked = true;
                token.RevokedAt = now;
            }
            if (!token.IsDeleted)
            {
                token.IsDeleted = true;
                token.DeletedAt = now;
            }
            token.DeviceInfo = null;
            token.IpAddress = null;
            await _cache.RemoveAsync(token.Token, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return tokens.Count;
    }
}
