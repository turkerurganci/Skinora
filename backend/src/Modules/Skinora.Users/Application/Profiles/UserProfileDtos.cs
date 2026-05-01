namespace Skinora.Users.Application.Profiles;

/// <summary>
/// Matches 07 §5.1 (U1 — <c>GET /users/me</c>) response <c>data</c> shape
/// verbatim. Field ordering and naming are part of the API contract; don't
/// reorder without updating 07.
/// </summary>
/// <remarks>
/// <para><c>reputationScore</c> is the composite 06 §3.1 score computed at
/// read time by <c>IReputationScoreCalculator</c>; null when either
/// reputation threshold (account age, completed-tx count) is not satisfied.
/// <c>cancelRate</c> is the complement of
/// <c>successfulTransactionRate</c> (also null when the rate is null).
/// <c>accountAge</c> is a Turkish relative string — i18n (T97) will localise.
/// <c>mobileAuthenticatorActive</c> mirrors <c>User.MobileAuthenticatorVerified</c>;
/// the real MA check arrives with T64–T69 via the Steam sidecar.</para>
/// </remarks>
public sealed record UserProfileDto(
    Guid Id,
    string SteamId,
    string DisplayName,
    string? AvatarUrl,
    string AccountAge,
    DateTime CreatedAt,
    decimal? ReputationScore,
    int CompletedTransactionCount,
    decimal? SuccessfulTransactionRate,
    decimal? CancelRate,
    string? SellerWalletAddress,
    string? RefundWalletAddress,
    bool MobileAuthenticatorActive);

/// <summary>
/// Matches 07 §5.2 (U2 — <c>GET /users/me/stats</c>) response <c>data</c>
/// shape — dashboard quick stats (S05).
/// </summary>
public sealed record UserStatsDto(
    int CompletedTransactionCount,
    decimal? SuccessfulTransactionRate,
    decimal? ReputationScore);

/// <summary>
/// Matches 07 §5.5 (U5 — <c>GET /users/:steamId</c>) response <c>data</c>
/// shape — public profile, no wallet addresses, no cancel rate.
/// </summary>
public sealed record PublicUserProfileDto(
    string SteamId,
    string DisplayName,
    string? AvatarUrl,
    string AccountAge,
    decimal? ReputationScore,
    int CompletedTransactionCount,
    decimal? SuccessfulTransactionRate);
