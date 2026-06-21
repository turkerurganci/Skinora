using Microsoft.EntityFrameworkCore;
using Skinora.Admin.Domain.Entities;
using Skinora.Shared.Models;
using Skinora.Shared.Persistence;
using Skinora.Users.Application.Profiles;
using Skinora.Users.Application.Reputation;
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
/// AD16 stat / flag / dispute / counterparty aggregations are wired (T105) via
/// <see cref="IAdminUserActivityProvider"/> — its implementation lives at the
/// API composition root because those reads span Skinora.Transactions /
/// Skinora.Fraud / Skinora.Disputes (modules this one cannot reference).
/// Reputation is computed on demand via <see cref="IReputationScoreCalculator"/>;
/// the profile also surfaces its breakdown (completed count / success / cancel
/// rate — 04 §8.9.1). Past wallet addresses come from the append-only
/// WalletAddressHistory (T105b — see <see cref="BuildWalletEntriesAsync"/>).
/// </remarks>
public sealed class AdminUserService : IAdminUserService
{
    private const int MinPage = 1;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly IReputationScoreCalculator _reputation;
    private readonly IAdminUserActivityProvider _activity;

    public AdminUserService(
        AppDbContext db,
        TimeProvider clock,
        IReputationScoreCalculator reputation,
        IAdminUserActivityProvider activity)
    {
        _db = db;
        _clock = clock;
        _reputation = reputation;
        _activity = activity;
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
            var pattern = $"%{SqlLikeEscaper.Escape(term)}%";
            baseQuery = users.Where(u =>
                EF.Functions.Like(u.SteamDisplayName, pattern) ||
                EF.Functions.Like(u.SteamId, pattern));
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

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        // Cross-module activity (transactions / flags / disputes / counterparties)
        // + the two conditional badge signals — aggregated at the composition
        // root (07 §9.16).
        var activity = await _activity.GetAsync(user.Id, cancellationToken);

        // Reputation is computed on demand from the denormalized counters so
        // admin-tunable thresholds take effect without a backfill (06 §3.1);
        // null = "Yeni kullanıcı".
        var reputationScore = await _reputation.ComputeAsync(
            user.CompletedTransactionCount,
            user.SuccessfulTransactionRate,
            user.CreatedAt,
            nowUtc,
            cancellationToken);

        // Deactivated (user self-deactivation, blocks login) takes precedence
        // over Suspended (admin-enforced restricted session) — T105a.
        var status = user.IsDeactivated
            ? AdminAccountStatus.Deactivated
            : user.IsSuspended
                ? AdminAccountStatus.Suspended
                : AdminAccountStatus.Active;

        var profile = new AdminUserDetailProfileDto(
            Id: user.Id,
            SteamId: user.SteamId,
            DisplayName: user.SteamDisplayName,
            AvatarUrl: user.SteamAvatarUrl,
            AccountStatus: status,
            AccountAge: AccountAgeFormatter.Format(user.CreatedAt, nowUtc),
            CreatedAt: user.CreatedAt,
            ReputationScore: reputationScore,
            IsSuspended: user.IsSuspended,
            SuspendedAt: user.SuspendedAt,
            SuspensionReason: user.SuspensionReason,
            SuspensionExpiresAt: user.SuspensionExpiresAt,
            ActiveTransactionCount: activity.ActiveTransactionCount,
            HasTransactionOnHold: activity.HasTransactionOnHold,
            // 04 §8.9.1 reputation breakdown — same fraction convention as
            // UserProfileDto (07 §5.1). CancelRate = complement of the rate.
            CompletedTransactionCount: user.CompletedTransactionCount,
            SuccessfulTransactionRate: user.SuccessfulTransactionRate,
            CancelRate: user.SuccessfulTransactionRate is null
                ? null
                : 1m - user.SuccessfulTransactionRate.Value);

        var stats = new AdminUserDetailStatsDto(
            TotalTransactions: activity.TotalTransactions,
            CompletedTransactions: activity.CompletedTransactions,
            CancelledTransactions: activity.CancelledTransactions,
            FlaggedTransactions: activity.FlaggedTransactions,
            SuccessfulTransactionRate: user.SuccessfulTransactionRate,
            TotalVolume: activity.TotalVolume,
            LastTransactionAt: activity.LastTransactionAt);

        var walletHistory = await BuildWalletEntriesAsync(user, cancellationToken);

        return new AdminUserDetailDto(
            Profile: profile,
            Stats: stats,
            WalletHistory: walletHistory,
            FlagHistory: activity.FlagHistory,
            DisputeHistory: activity.DisputeHistory,
            FrequentCounterparties: activity.FrequentCounterparties);
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
    /// AD16 walletHistory[] (04 §8.9.3): current addresses (<c>current = true</c>)
    /// come from the <see cref="User"/> row; previous addresses
    /// (<c>current = false</c>, newest first) come from the append-only
    /// WalletAddressHistory recorded on each change (T105b). "current" is never a
    /// stored flag — it is derived here, so the history table stays append-only.
    /// </summary>
    private async Task<List<AdminUserWalletEntryDto>> BuildWalletEntriesAsync(
        User user, CancellationToken cancellationToken)
    {
        var entries = new List<AdminUserWalletEntryDto>();

        if (!string.IsNullOrEmpty(user.DefaultPayoutAddress))
        {
            entries.Add(new AdminUserWalletEntryDto(
                Type: AdminWalletEntryType.Seller,
                Address: user.DefaultPayoutAddress,
                SetAt: user.PayoutAddressChangedAt,
                Current: true));
        }
        if (!string.IsNullOrEmpty(user.DefaultRefundAddress))
        {
            entries.Add(new AdminUserWalletEntryDto(
                Type: AdminWalletEntryType.Buyer,
                Address: user.DefaultRefundAddress,
                SetAt: user.RefundAddressChangedAt,
                Current: true));
        }

        // Superseded addresses, newest replacement first. Type is stored as the
        // AD16 wire value ("seller"/"buyer") so it projects straight through.
        var past = await _db.Set<WalletAddressHistory>()
            .AsNoTracking()
            .Where(h => h.UserId == user.Id)
            .OrderByDescending(h => h.Id)
            .Select(h => new AdminUserWalletEntryDto(h.Type, h.Address, h.SetAt, false))
            .ToListAsync(cancellationToken);

        entries.AddRange(past);
        return entries;
    }
}
