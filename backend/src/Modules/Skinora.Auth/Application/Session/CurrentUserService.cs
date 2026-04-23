using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Application.Session;

/// <summary>
/// Builds the <c>GET /auth/me</c> response DTO from the authenticated user's
/// DB row and role claim — 07 §4.5.
/// </summary>
public interface ICurrentUserService
{
    Task<CurrentUserDto?> GetAsync(Guid userId, string role, CancellationToken cancellationToken);
}

/// <summary>
/// Matches the 07 §4.5 response <c>data</c> shape verbatim. Field ordering and
/// naming are part of the API contract; don't reorder without updating 07.
/// </summary>
public sealed record CurrentUserDto(
    Guid Id,
    string SteamId,
    string DisplayName,
    string? AvatarUrl,
    bool MobileAuthenticatorActive,
    bool TosAccepted,
    string Role,
    string Language,
    bool HasSellerWallet,
    bool HasRefundWallet,
    DateTime CreatedAt);

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly AppDbContext _db;

    public CurrentUserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CurrentUserDto?> GetAsync(
        Guid userId, string role, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null) return null;

        return new CurrentUserDto(
            Id: user.Id,
            SteamId: user.SteamId,
            DisplayName: user.SteamDisplayName,
            AvatarUrl: user.SteamAvatarUrl,
            MobileAuthenticatorActive: user.MobileAuthenticatorVerified,
            TosAccepted: user.TosAcceptedAt is not null,
            Role: role,
            Language: user.PreferredLanguage,
            HasSellerWallet: !string.IsNullOrWhiteSpace(user.DefaultPayoutAddress),
            HasRefundWallet: !string.IsNullOrWhiteSpace(user.DefaultRefundAddress),
            CreatedAt: user.CreatedAt);
    }
}
