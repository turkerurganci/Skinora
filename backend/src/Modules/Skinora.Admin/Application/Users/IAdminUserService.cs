using Skinora.Shared.Models;

namespace Skinora.Admin.Application.Users;

/// <summary>
/// Backing service for 07 §9.15–§9.18 — admin-facing user list/detail
/// and role assignment endpoints. <c>FlagHistory</c>, <c>DisputeHistory</c>
/// and <c>FrequentCounterparties</c> ride along as empty arrays in T39 —
/// the actual aggregations land with T54 (Fraud), T58 (Dispute) and T63
/// (admin transactions read service). The contract is shipped now so the
/// frontend (T105 / S20) can wire the response shape end-to-end.
/// </summary>
public interface IAdminUserService
{
    /// <summary>AD15 — paginated admin user list, optional name search + role filter.</summary>
    Task<PagedResult<AdminUserListItemDto>> ListAsync(
        string? search,
        Guid? roleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>AD16 — user detail (profile + stats + wallet/flag/dispute/counterparty history).</summary>
    Task<AdminUserDetailDto?> GetDetailAsync(
        string steamId, CancellationToken cancellationToken);

    /// <summary>AD16b — paginated transaction history scoped to the resolved user (T63 will fill).</summary>
    Task<PagedResult<object>?> GetTransactionsAsync(
        string steamId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>AD17 — assign / change / clear (<c>roleId == null</c>) the user's admin role.</summary>
    Task<AssignRoleOutcome> AssignRoleAsync(
        Guid userId,
        AssignRoleRequest request,
        Guid? assigningAdminId,
        CancellationToken cancellationToken);
}
