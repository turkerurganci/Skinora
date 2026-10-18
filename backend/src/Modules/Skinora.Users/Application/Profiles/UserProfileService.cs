using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Application.Reputation;
using Skinora.Users.Application.Wallet;
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
    private readonly IReputationScoreCalculator _reputation;

    public UserProfileService(
        AppDbContext db,
        TimeProvider clock,
        IReputationScoreCalculator reputation)
    {
        _db = db;
        _clock = clock;
        _reputation = reputation;
    }

    public async Task<UserProfileDto?> GetOwnProfileAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeactivated, cancellationToken);

        if (user is null) return null;

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var reputationScore = await _reputation.ComputeAsync(
            user.CompletedTransactionCount,
            user.SuccessfulTransactionRate,
            user.CreatedAt,
            nowUtc,
            cancellationToken);

        // T146 — saved addresses, one row per (network, type) (06 §1.4).
        var walletRows = await _db.Set<UserWalletAddress>()
            .AsNoTracking()
            .Where(w => w.UserId == user.Id)
            .ToListAsync(cancellationToken);

        var wallets = walletRows
            .OrderBy(w => w.Network)
            .ThenBy(w => w.Type)
            .Select(w => new UserWalletAddressDto(
                w.Network.ToString(),
                w.Type.ToString(),
                BlockchainAddress.ToDisplay(w.Address)!,
                w.ChangedAt))
            .ToList();

        // Pre-F8 fields (07 §5.1): the TRON addresses only.
        string? TronAddressOf(WalletAddressType type) => walletRows
            .FirstOrDefault(w => w.Network == BlockchainNetwork.TRON && w.Type == type)?.Address;

        return new UserProfileDto(
            Id: user.Id,
            SteamId: user.SteamId,
            DisplayName: user.SteamDisplayName,
            AvatarUrl: user.SteamAvatarUrl,
            AccountAge: AccountAgeFormatter.Format(user.CreatedAt, nowUtc),
            CreatedAt: user.CreatedAt,
            ReputationScore: reputationScore,
            CompletedTransactionCount: user.CompletedTransactionCount,
            SuccessfulTransactionRate: user.SuccessfulTransactionRate,
            CancelRate: CancelRateFrom(user.SuccessfulTransactionRate),
            SellerWalletAddress: TronAddressOf(WalletAddressType.PAYOUT),
            RefundWalletAddress: TronAddressOf(WalletAddressType.REFUND),
            MobileAuthenticatorActive: user.MobileAuthenticatorVerified,
            // T119a — accept-form prefill source (07 §7.6). Already normalized
            // by U17 (SteamTradeUrlService), so it round-trips through the
            // accept endpoint's parser unchanged.
            SteamTradeUrl: user.SteamTradeUrl,
            Wallets: wallets);
    }

    public async Task<UserStatsDto?> GetOwnStatsAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId && !u.IsDeactivated)
            .Select(u => new
            {
                u.CompletedTransactionCount,
                u.SuccessfulTransactionRate,
                u.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null) return null;

        var reputationScore = await _reputation.ComputeAsync(
            user.CompletedTransactionCount,
            user.SuccessfulTransactionRate,
            user.CreatedAt,
            _clock.GetUtcNow().UtcDateTime,
            cancellationToken);

        return new UserStatsDto(
            user.CompletedTransactionCount,
            user.SuccessfulTransactionRate,
            reputationScore);
    }

    public async Task<PublicUserProfileDto?> GetPublicProfileAsync(
        string steamId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.SteamId == steamId && !u.IsDeactivated, cancellationToken);

        if (user is null) return null;

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var reputationScore = await _reputation.ComputeAsync(
            user.CompletedTransactionCount,
            user.SuccessfulTransactionRate,
            user.CreatedAt,
            nowUtc,
            cancellationToken);

        return new PublicUserProfileDto(
            SteamId: user.SteamId,
            DisplayName: user.SteamDisplayName,
            AvatarUrl: user.SteamAvatarUrl,
            AccountAge: AccountAgeFormatter.Format(user.CreatedAt, nowUtc),
            ReputationScore: reputationScore,
            CompletedTransactionCount: user.CompletedTransactionCount,
            SuccessfulTransactionRate: user.SuccessfulTransactionRate);
    }

    // 07 §5.1 emits cancelRate as the complement of successfulTransactionRate
    // (M1 closure: 06 fraction is canonical, both fields are 0..1).
    private static decimal? CancelRateFrom(decimal? successRate) =>
        successRate is null ? null : 1m - successRate.Value;
}
