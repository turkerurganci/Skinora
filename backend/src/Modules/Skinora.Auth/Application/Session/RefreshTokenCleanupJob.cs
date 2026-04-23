using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Auth.Domain.Entities;
using Skinora.Shared.Persistence;

namespace Skinora.Auth.Application.Session;

/// <summary>
/// Recurring cleanup for expired or revoked <see cref="RefreshToken"/> rows —
/// task T32 acceptance criterion "Expired/revoked token cleanup (periyodik)".
/// </summary>
/// <remarks>
/// <para>
/// Tokens are soft-deleted (global query filter hides them from normal reads)
/// once they are past their expiry or have been revoked for longer than the
/// grace window. The grace window keeps recent revocations visible for audit
/// while still bounding table growth.
/// </para>
/// <para>
/// Hangfire expression serialization constrains the handler signature to
/// <c>Expression&lt;Action&lt;T&gt;&gt;</c>, so <see cref="Execute"/> blocks on
/// the async core. The job runs at most once per day so tying up a worker
/// thread for the duration of a single sweep is acceptable.
/// </para>
/// </remarks>
public sealed class RefreshTokenCleanupJob
{
    public const string RecurringJobId = "refresh-token-cleanup";

    /// <summary>Grace window past expiry or revocation before soft-delete.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromDays(7);

    private readonly AppDbContext _db;
    private readonly ILogger<RefreshTokenCleanupJob> _logger;

    public RefreshTokenCleanupJob(AppDbContext db, ILogger<RefreshTokenCleanupJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public void Execute() => ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var threshold = DateTime.UtcNow - GracePeriod;

        var stale = await _db.Set<RefreshToken>()
            .Where(t => t.ExpiresAt < threshold
                        || (t.IsRevoked && t.RevokedAt != null && t.RevokedAt < threshold))
            .ToListAsync(cancellationToken);

        if (stale.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var token in stale)
        {
            token.IsDeleted = true;
            token.DeletedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "RefreshTokenCleanupJob soft-deleted {Count} refresh token(s) past the {GraceDays}d grace window.",
            stale.Count, GracePeriod.TotalDays);
        return stale.Count;
    }
}
