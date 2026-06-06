using Skinora.Shared.Enums;

namespace Skinora.API.Services.UserSuspension;

/// <summary>
/// T105a admin account-suspension service (02 §14.0/§16.2, 03 §2.1/§8.3).
/// Suspension is the admin-enforced restricted-session state: a suspended user
/// can still log in and read, but fund-flow mutations are rejected. A temporary
/// block sets an expiry that the <c>AutoUnsuspendJob</c> lifts automatically.
/// </summary>
public interface IAdminUserSuspensionService
{
    /// <summary>AD20 — suspend a user. <paramref name="durationDays"/> via the request: null = permanent.</summary>
    Task<SuspendUserOutcome> SuspendAsync(
        Guid adminUserId,
        Guid targetUserId,
        SuspendUserRequest request,
        string? ipAddress,
        CancellationToken cancellationToken);

    /// <summary>
    /// AD21 — lift a suspension. Admin path: <paramref name="actorType"/> = ADMIN,
    /// <paramref name="automatic"/> = false. The temp-block expiry job calls with
    /// <paramref name="actorType"/> = SYSTEM, <paramref name="automatic"/> = true.
    /// </summary>
    Task<UnsuspendUserOutcome> UnsuspendAsync(
        Guid actorUserId,
        Guid targetUserId,
        ActorType actorType,
        bool automatic,
        string? ipAddress,
        CancellationToken cancellationToken);
}

public sealed record SuspendUserRequest(string? Reason, int? DurationDays);

public sealed record SuspendUserResponse(
    Guid UserId,
    DateTime SuspendedAt,
    string Reason,
    DateTime? ExpiresAt);

public enum SuspendUserStatus
{
    Suspended,
    NotFound,
    ValidationFailed,
    AlreadySuspended,
}

public sealed record SuspendUserOutcome(
    SuspendUserStatus Status,
    SuspendUserResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record UnsuspendUserResponse(Guid UserId, DateTime UnsuspendedAt);

public enum UnsuspendUserStatus
{
    Unsuspended,
    NotFound,
    NotSuspended,
}

public sealed record UnsuspendUserOutcome(
    UnsuspendUserStatus Status,
    UnsuspendUserResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

public static class UserSuspensionErrorCodes
{
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string AlreadySuspended = "ALREADY_SUSPENDED";
    public const string NotSuspended = "NOT_SUSPENDED";
}
