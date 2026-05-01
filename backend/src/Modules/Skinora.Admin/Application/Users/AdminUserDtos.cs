namespace Skinora.Admin.Application.Users;

/// <summary>One row of 07 §9.15 — <c>GET /admin/users</c>.</summary>
public sealed record AdminUserListItemDto(
    Guid Id,
    string SteamId,
    string DisplayName,
    string? AvatarUrl,
    AdminUserAssignedRoleDto? Role);

/// <summary>Inline role summary used by 07 §9.15 / §9.18.</summary>
public sealed record AdminUserAssignedRoleDto(Guid Id, string Name);

/// <summary>Body of 07 §9.16 — <c>GET /admin/users/:steamId</c>.</summary>
public sealed record AdminUserDetailDto(
    AdminUserDetailProfileDto Profile,
    AdminUserDetailStatsDto Stats,
    IReadOnlyList<AdminUserWalletEntryDto> WalletHistory,
    IReadOnlyList<AdminUserFlagEntryDto> FlagHistory,
    IReadOnlyList<AdminUserDisputeEntryDto> DisputeHistory,
    IReadOnlyList<AdminUserCounterpartyDto> FrequentCounterparties);

public sealed record AdminUserDetailProfileDto(
    Guid Id,
    string SteamId,
    string DisplayName,
    string? AvatarUrl,
    string AccountStatus,
    string AccountAge,
    DateTime CreatedAt,
    decimal? ReputationScore);

public sealed record AdminUserDetailStatsDto(
    int TotalTransactions,
    int CompletedTransactions,
    int CancelledTransactions,
    int FlaggedTransactions,
    decimal? SuccessfulTransactionRate,
    string? TotalVolume,
    DateTime? LastTransactionAt);

public sealed record AdminUserWalletEntryDto(
    string Type,
    string Address,
    DateTime? SetAt,
    bool Current);

public sealed record AdminUserFlagEntryDto(
    Guid Id,
    string Type,
    Guid TransactionId,
    string ReviewStatus,
    DateTime CreatedAt);

public sealed record AdminUserDisputeEntryDto(
    Guid Id,
    string Type,
    Guid TransactionId,
    string Status,
    DateTime CreatedAt);

public sealed record AdminUserCounterpartyDto(
    string SteamId,
    string DisplayName,
    int TransactionCount,
    DateTime? LastTransactionAt);

/// <summary>Body of 07 §9.18 — <c>PUT /admin/users/:id/role</c>.</summary>
public sealed record AssignRoleRequest(Guid? RoleId);

/// <summary>Body of 07 §9.18 success response.</summary>
public sealed record AssignRoleResponse(
    Guid UserId,
    AdminUserAssignedRoleDto? Role,
    DateTime? AssignedAt);

/// <summary>Stable account-status string for AD16 <c>profile.accountStatus</c>.</summary>
public static class AdminAccountStatus
{
    public const string Active = "ACTIVE";
    public const string Deactivated = "DEACTIVATED";
    public const string Deleted = "DELETED";
}

/// <summary>Stable wallet-entry type for AD16 <c>walletHistory</c> rows.</summary>
public static class AdminWalletEntryType
{
    public const string Seller = "seller";
    public const string Buyer = "buyer";
}
