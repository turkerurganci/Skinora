namespace Skinora.Users.Application.Profiles;

/// <summary>
/// Serves profile DTOs backing 07 §5.1 (U1), §5.2 (U2) and §5.5 (U5).
/// <c>null</c> returns signal a missing/soft-deleted user; callers map to the
/// appropriate HTTP status (U1/U2 → 401, U5 → 404 USER_NOT_FOUND).
/// </summary>
public interface IUserProfileService
{
    Task<UserProfileDto?> GetOwnProfileAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserStatsDto?> GetOwnStatsAsync(Guid userId, CancellationToken cancellationToken);

    Task<PublicUserProfileDto?> GetPublicProfileAsync(string steamId, CancellationToken cancellationToken);
}
