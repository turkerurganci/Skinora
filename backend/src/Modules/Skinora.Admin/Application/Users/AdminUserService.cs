using Microsoft.EntityFrameworkCore;
using Skinora.Admin.Domain.Entities;
using Skinora.Shared.Models;
using Skinora.Shared.Persistence;
using Skinora.Users.Application.Profiles;
using Skinora.Users.Domain.Entities;

namespace Skinora.Admin.Application.Users;

/// <summary>
/// EF Core-backed implementation of 07 §9.15–§9.18.
/// </summary>
/// <remarks>
/// AD15 surfaces both browse and search use cases of S19:
/// <list type="bullet">
///   <item><b>Default / <c>roleId</c></b> — admin user list (only users with an
///         active <see cref="AdminUserRole"/> assignment). Filter by role if
///         provided. Order by display name.</item>
///   <item><b><c>search</c></b> — broadens to all non-deactivated users so the
///         admin can locate a non-admin to promote via AD17.</item>
/// </list>
/// AD16 stat / history aggregations land with downstream tasks (T54 fraud,
/// T58 dispute, T63 admin transactions read service); the empty-array
/// placeholders here ship the contract end-to-end so frontend (T105) can
/// wire response handling against the final shape.
/// </remarks>
public sealed class AdminUserService : IAdminUserService
{
    private const int MinPage = 1;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public AdminUserService(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<PagedResult<AdminUserListItemDto>> ListAsync(
        string? search,
        Guid? roleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var safePage = page < MinPage ? MinPage : page;
        var safePageSize = pageSize < MinPageSize
            ? DefaultPageSize
            : pageSize > MaxPageSize ? MaxPageSize : pageSize;

        // Active-role-assignment lookup keyed by user. AdminUserRole already
        // hides soft-deleted rows via its query filter.
        var assignmentsQuery = _db.Set<AdminUserRole>().AsNoTracking();
        if (roleId.HasValue)
            assignmentsQuery = assignmentsQuery.Where(ur => ur.AdminRoleId == roleId.Value);

        var assignments = assignmentsQuery
            .Select(ur => new { ur.UserId, ur.AdminRoleId });

        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var users = _db.Set<User>().AsNoTracking().Where(u => !u.IsDeactivated);

        IQueryable<User> baseQuery;
        if (hasSearch)
        {
            // Search broadens beyond admins so AD17 can target any user
            // (07 §9.15 doesn't pin the search behaviour; this matches the S19
            // "rol atama" workflow which needs to find non-admins).
            var term = search!.Trim();
            baseQuery = users.Where(u =>
                EF.Functions.Like(u.SteamDisplayName, $"%{term}%") ||
                EF.Functions.Like(u.SteamId, $"%{term}%"));
            if (roleId.HasValue)
                baseQuery = baseQuery.Where(u => assignments.Any(a => a.UserId == u.Id));
        }
        else
        {
            // No search → admin browse only. Restrict to users with at least
            // one active assignment (filtered by roleId when provided).
            baseQuery = users.Where(u => assignments.Any(a => a.UserId == u.Id));
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var rows = await baseQuery
            .OrderBy(u => u.SteamDisplayName)
            .ThenBy(u => u.Id)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(u => new
            {
                u.Id,
                u.SteamId,
                u.SteamDisplayName,
                u.SteamAvatarUrl,
            })
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(r => r.Id).ToList();
        var roleByUser = await (
            from ur in _db.Set<AdminUserRole>().AsNoTracking()
            join r in _db.Set<AdminRole>().AsNoTracking()
                on ur.AdminRoleId equals r.Id
            where userIds.Contains(ur.UserId)
            select new { ur.UserId, RoleId = r.Id, r.Name })
            .ToListAsync(cancellationToken);

        var byUserId = roleByUser.ToDictionary(x => x.UserId, x => (x.RoleId, x.Name));

        var items = rows
            .Select(r => new AdminUserListItemDto(
                Id: r.Id,
                SteamId: r.SteamId,
                DisplayName: r.SteamDisplayName,
                AvatarUrl: r.SteamAvatarUrl,
                Role: byUserId.TryGetValue(r.Id, out var assignment)
                    ? new AdminUserAssignedRoleDto(assignment.RoleId, assignment.Name)
                    : null))
            .ToList();

        return new PagedResult<AdminUserListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = safePage,
            PageSize = safePageSize,
        };
    }

    public async Task<AdminUserDetailDto?> GetDetailAsync(
        string steamId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(steamId)) return null;

        // Soft-delete query filter on User already excludes anonymized
        // ("DELETED") rows — only ACTIVE / DEACTIVATED users surface here.
        // The DELETED account-status is documented as a forward-compat
        // placeholder (T39 known limitation).
        var user = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SteamId == steamId, cancellationToken);

        if (user is null) return null;

        var status = user.IsDeactivated
            ? AdminAccountStatus.Deactivated
            : AdminAccountStatus.Active;

        var profile = new AdminUserDetailProfileDto(
            Id: user.Id,
            SteamId: user.SteamId,
            DisplayName: user.SteamDisplayName,
            AvatarUrl: user.SteamAvatarUrl,
            AccountStatus: status,
            AccountAge: AccountAgeFormatter.Format(
                user.CreatedAt, _clock.GetUtcNow().UtcDateTime),
            CreatedAt: user.CreatedAt,
            // T43 forward devir — reputation calculation lands with the
            // dedicated task; until then admins see null (06 §3.1).
            ReputationScore: null);

        var stats = new AdminUserDetailStatsDto(
            // T63 forward devir — admin transaction aggregates land with the
            // admin transactions read service. Denormalized counters on User
            // (CompletedTransactionCount) are the only signal available now.
            TotalTransactions: user.CompletedTransactionCount,
            CompletedTransactions: user.CompletedTransactionCount,
            CancelledTransactions: 0,
            FlaggedTransactions: 0,
            SuccessfulTransactionRate: user.SuccessfulTransactionRate,
            TotalVolume: null,
            LastTransactionAt: null);

        var walletHistory = BuildCurrentWalletEntries(user).ToList();

        return new AdminUserDetailDto(
            Profile: profile,
            Stats: stats,
            WalletHistory: walletHistory,
            // T54 / T58 / T63 forward devir — empty until the backing
            // services land (07 §9.16 contract shipped now so the frontend
            // can wire response parsing).
            FlagHistory: [],
            DisputeHistory: [],
            FrequentCounterparties: []);
    }

    public async Task<PagedResult<object>?> GetTransactionsAsync(
        string steamId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(steamId)) return null;

        var safePage = page < MinPage ? MinPage : page;
        var safePageSize = pageSize < MinPageSize
            ? DefaultPageSize
            : pageSize > MaxPageSize ? MaxPageSize : pageSize;

        var exists = await _db.Set<User>()
            .AsNoTracking()
            .AnyAsync(u => u.SteamId == steamId, cancellationToken);
        if (!exists) return null;

        // T63 forward devir — backing transaction read service lands with
        // the admin transactions task. Empty paginated payload preserves
        // the 07 §9.17 contract.
        return new PagedResult<object>
        {
            Items = [],
            TotalCount = 0,
            Page = safePage,
            PageSize = safePageSize,
        };
    }

    public async Task<AssignRoleOutcome> AssignRoleAsync(
        Guid userId,
        AssignRoleRequest request,
        Guid? assigningAdminId,
        CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return new AssignRoleOutcome.UserNotFound();

        AdminRole? newRole = null;
        if (request.RoleId.HasValue)
        {
            newRole = await _db.Set<AdminRole>()
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == request.RoleId.Value, cancellationToken);
            if (newRole is null) return new AssignRoleOutcome.RoleNotFound();
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        // Tombstone every active assignment for this user. The unique filtered
        // index on (UserId, AdminRoleId) WHERE IsDeleted = 0 means we can
        // freely re-insert the same role later without conflict.
        var existing = await _db.Set<AdminUserRole>()
            .Where(ur => ur.UserId == userId)
            .ToListAsync(cancellationToken);
        foreach (var assignment in existing)
        {
            assignment.IsDeleted = true;
            assignment.DeletedAt = now;
        }

        AdminUserRole? newAssignment = null;
        if (newRole is not null)
        {
            newAssignment = new AdminUserRole
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AdminRoleId = newRole.Id,
                AssignedAt = now,
                AssignedByAdminId = assigningAdminId,
            };
            _db.Set<AdminUserRole>().Add(newAssignment);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var response = new AssignRoleResponse(
            UserId: userId,
            Role: newRole is null
                ? null
                : new AdminUserAssignedRoleDto(newRole.Id, newRole.Name),
            AssignedAt: newAssignment?.AssignedAt);

        return new AssignRoleOutcome.Success(response);
    }

    /// <summary>
    /// AD16 walletHistory[]: T34 carries only current addresses + change
    /// timestamps on <see cref="User"/>; a separate WalletAddressHistory
    /// entity isn't part of the data model. Emit one entry per non-null
    /// current address with <c>current = true</c> (T39 known limitation —
    /// historical past-addresses arrive when/if the schema gains a
    /// dedicated history table).
    /// </summary>
    private static IEnumerable<AdminUserWalletEntryDto> BuildCurrentWalletEntries(User user)
    {
        if (!string.IsNullOrEmpty(user.DefaultPayoutAddress))
        {
            yield return new AdminUserWalletEntryDto(
                Type: AdminWalletEntryType.Seller,
                Address: user.DefaultPayoutAddress,
                SetAt: user.PayoutAddressChangedAt,
                Current: true);
        }
        if (!string.IsNullOrEmpty(user.DefaultRefundAddress))
        {
            yield return new AdminUserWalletEntryDto(
                Type: AdminWalletEntryType.Buyer,
                Address: user.DefaultRefundAddress,
                SetAt: user.RefundAddressChangedAt,
                Current: true);
        }
    }
}
