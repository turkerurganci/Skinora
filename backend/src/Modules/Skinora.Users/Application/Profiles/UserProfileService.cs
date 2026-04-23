using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Profiles;

/// <summary>
/// EF Core-backed read model for user profile endpoints (07 §5.1, §5.2, §5.5).
/// Soft-deleted / deactivated users are excluded — the global query filter on
/// <see cref="User"/> already drops <c>IsDeleted</c> rows, and deactivated
/// accounts are treated as not-found to keep 07 §5.5 and the internal /users/me
/// response consistent when an admin deactivates mid-session.
/// </summary>
public sealed class UserProfileService : IUserProfileService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public UserProfileService(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<UserProfileDto?> GetOwnProfileAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeactivated, cancellationToken);

        if (user is null) return null;

        return new UserProfileDto(
            Id: user.Id,
            SteamId: user.SteamId,
            DisplayName: user.SteamDisplayName,
            AvatarUrl: user.SteamAvatarUrl,
            AccountAge: AccountAgeFormatter.Format(user.CreatedAt, _clock.GetUtcNow().UtcDateTime),
            CreatedAt: user.CreatedAt,
            ReputationScore: null,
            CompletedTransactionCount: user.CompletedTransactionCount,
            SuccessfulTransactionRate: user.SuccessfulTransactionRate,
            CancelRate: null,
            SellerWalletAddress: user.DefaultPayoutAddress,
            RefundWalletAddress: user.DefaultRefundAddress,
            MobileAuthenticatorActive: user.MobileAuthenticatorVerified);
    }

    public async Task<UserStatsDto?> GetOwnStatsAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId && !u.IsDeactivated)
            .Select(u => new UserStatsDto(
                u.CompletedTransactionCount,
                u.SuccessfulTransactionRate,
                (decimal?)null))
            .FirstOrDefaultAsync(cancellationToken);

        return user;
    }

    public async Task<PublicUserProfileDto?> GetPublicProfileAsync(
        string steamId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.SteamId == steamId && !u.IsDeactivated, cancellationToken);

        if (user is null) return null;

        return new PublicUserProfileDto(
            SteamId: user.SteamId,
            DisplayName: user.SteamDisplayName,
            AvatarUrl: user.SteamAvatarUrl,
            AccountAge: AccountAgeFormatter.Format(user.CreatedAt, _clock.GetUtcNow().UtcDateTime),
            ReputationScore: null,
            CompletedTransactionCount: user.CompletedTransactionCount,
            SuccessfulTransactionRate: user.SuccessfulTransactionRate);
    }
}
