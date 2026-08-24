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
    /// <param name="permissions">
    /// The caller's <c>Permission</c> claims, passed in rather than re-resolved
    /// so the response describes the authority the bearer token actually
    /// carries. Empty for a non-admin — and also for a super admin, whose
    /// authorization short-circuits on role (see <see cref="CurrentUserDto.Permissions"/>).
    /// </param>
    Task<CurrentUserDto?> GetAsync(
        Guid userId,
        string role,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken);
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
    // WP11 — accepted ToS version (07 §4.5). null until first acceptance. The
    // client compares this against the current ToS version to decide whether a
    // re-acceptance prompt is required on a version bump (T30 reprompt).
    string? TosAcceptedVersion,
    string Role,
    string Language,
    bool HasSellerWallet,
    bool HasRefundWallet,
    DateTime CreatedAt,
    // T105a — restricted-session flag (02 §14.0, 03 §2.1). When true the client
    // renders the suspended session (SuspendedHeader + S03d) and fund-flow
    // mutations are rejected server-side.
    bool IsSuspended,
    // WP2c (FE-permission-guard) — the admin permission keys carried by the
    // caller's token, so the client can hide a surface the caller could not use
    // instead of letting them walk into a 403.
    //
    // NOT a security boundary: every admin endpoint still enforces its own
    // Permission:<KEY> policy server-side, which stays the authoritative check.
    //
    // Empty for a non-admin, and ALSO empty for a super admin —
    // PermissionAuthorizationHandler short-circuits on role=super_admin, so no
    // Permission claims are ever minted for one. A client must therefore treat
    // super_admin as holding everything rather than reading this list literally;
    // that rule lives in the frontend `hasPermission` helper.
    IReadOnlyList<string> Permissions);

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly AppDbContext _db;

    public CurrentUserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CurrentUserDto?> GetAsync(
        Guid userId,
        string role,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
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
            TosAcceptedVersion: user.TosAcceptedVersion,
            Role: role,
            Language: user.PreferredLanguage,
            HasSellerWallet: !string.IsNullOrWhiteSpace(user.DefaultPayoutAddress),
            HasRefundWallet: !string.IsNullOrWhiteSpace(user.DefaultRefundAddress),
            CreatedAt: user.CreatedAt,
            IsSuspended: user.IsSuspended,
            Permissions: permissions);
    }
}
