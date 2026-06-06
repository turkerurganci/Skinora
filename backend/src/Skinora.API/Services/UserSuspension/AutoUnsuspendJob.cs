using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Services.UserSuspension;

/// <summary>
/// T105a temporary-block expiry job. Scans for users whose
/// <see cref="User.SuspensionExpiresAt"/> has passed and lifts each suspension
/// via <see cref="IAdminUserSuspensionService.UnsuspendAsync"/> (actor = SYSTEM,
/// <c>automatic = true</c>) so the audit row + ACCOUNT_UNSUSPENDED notification
/// fire exactly as an admin unsuspend would. Permanent suspensions
/// (<c>SuspensionExpiresAt == null</c>) are never touched.
/// </summary>
public sealed class AutoUnsuspendJob
{
    public const string RecurringJobId = "auto-unsuspend";

    /// <summary>Every 6 hours — bounds temp-block over-hold to ≤ 6h past expiry.</summary>
    public const string Cron = "0 */6 * * *";

    private readonly AppDbContext _db;
    private readonly IAdminUserSuspensionService _suspension;
    private readonly TimeProvider _clock;
    private readonly ILogger<AutoUnsuspendJob> _logger;

    public AutoUnsuspendJob(
        AppDbContext db,
        IAdminUserSuspensionService suspension,
        TimeProvider clock,
        ILogger<AutoUnsuspendJob> logger)
    {
        _db = db;
        _suspension = suspension;
        _clock = clock;
        _logger = logger;
    }

    // Hangfire requires a synchronous Expression<Action<T>> entry point.
    public void Execute() => ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        var expiredUserIds = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.IsSuspended
                        && u.SuspensionExpiresAt != null
                        && u.SuspensionExpiresAt <= nowUtc
                        && !u.IsDeleted)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var lifted = 0;
        foreach (var userId in expiredUserIds)
        {
            var outcome = await _suspension.UnsuspendAsync(
                actorUserId: SeedConstants.SystemUserId,
                targetUserId: userId,
                actorType: ActorType.SYSTEM,
                automatic: true,
                ipAddress: null,
                cancellationToken);

            if (outcome.Status == UnsuspendUserStatus.Unsuspended)
                lifted++;
        }

        if (lifted > 0)
            _logger.LogInformation("AutoUnsuspendJob lifted {Count} expired suspension(s).", lifted);

        return lifted;
    }
}
