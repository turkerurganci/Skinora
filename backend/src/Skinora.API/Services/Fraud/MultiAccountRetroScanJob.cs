using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Skinora.Users.Application.MultiAccount;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Services.Fraud;

/// <summary>
/// WP4b multi-account retro-scan. Periodically re-runs the existing
/// <see cref="IMultiAccountDetector"/> across every wallet-bearing active
/// account, closing the "only fires at wallet-update" gap
/// (<c>WalletAddressService</c> is the sole event trigger — T56_REPORT.md:150):
/// a collision created by a NEW account never re-evaluates the OLD account
/// (its wallet did not change), and collisions predating the detector are never
/// caught. This job re-evaluates them on a schedule.
/// </summary>
/// <remarks>
/// <para>
/// <b>No new detection logic.</b> The detector is the reusable seam — the job
/// only enumerates candidates and calls <see cref="IMultiAccountDetector.EvaluateAsync"/>
/// per user. It lives in <c>Skinora.API</c> (not <c>Skinora.Fraud</c>, which has
/// no Hangfire dependency) alongside the other recurring jobs, mirroring
/// <c>AutoUnsuspendJob</c>.
/// </para>
/// <para>
/// <b>Scan universe (WP4b owner decision):</b> non-deleted, non-deactivated
/// users that carry at least one wallet address (payout or refund). A user with
/// no wallet address can never produce the detector's strong signal, so it is
/// skipped — and both sides of any address collision necessarily carry the
/// shared address, so neither side is missed.
/// </para>
/// <para>
/// <b>Dedup (coarse, by design):</b> the detector's own idempotency gate skips
/// any user already carrying a non-rejected <c>MULTI_ACCOUNT</c> flag
/// (<c>MultiAccountDetector</c>), so a daily sweep never spams duplicate flags.
/// A genuinely new distinct link discovered after an admin <em>rejected</em> the
/// prior flag still re-flags (intended).
/// </para>
/// <para>
/// <b>Resilience:</b> a per-user failure is logged and swallowed so one bad row
/// never aborts the sweep; cancellation propagates. The detector owns its own
/// <c>SaveChanges</c> per user, so a fault leaves earlier users' flags intact.
/// </para>
/// </remarks>
public sealed class MultiAccountRetroScanJob
{
    public const string RecurringJobId = "multi-account-retro-scan";

    /// <summary>
    /// Daily at 02:00 UTC — off-peak. Multi-account links are slow-moving
    /// (wallet addresses rarely change), so a daily retroactive sweep is ample;
    /// the wallet-update event hook still catches new collisions in real time.
    /// </summary>
    public const string Cron = "0 2 * * *";

    private readonly AppDbContext _db;
    private readonly IMultiAccountDetector _detector;
    private readonly ILogger<MultiAccountRetroScanJob> _logger;

    public MultiAccountRetroScanJob(
        AppDbContext db,
        IMultiAccountDetector detector,
        ILogger<MultiAccountRetroScanJob> logger)
    {
        _db = db;
        _detector = detector;
        _logger = logger;
    }

    // Hangfire requires a synchronous Expression<Action<T>> entry point.
    public void Execute() => ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();

    public async Task<MultiAccountRetroScanOutcome> ExecuteAsync(CancellationToken cancellationToken)
    {
        var candidateIds = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => !u.IsDeleted
                        && !u.IsDeactivated
                        && ((u.DefaultPayoutAddress != null && u.DefaultPayoutAddress != "")
                            || (u.DefaultRefundAddress != null && u.DefaultRefundAddress != "")))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var flagged = 0;
        var alreadyFlagged = 0;
        var noSignal = 0;
        var failed = 0;

        foreach (var userId in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await _detector.EvaluateAsync(userId, cancellationToken);
                switch (result.Status)
                {
                    case MultiAccountEvaluationStatus.Flagged:
                        flagged++;
                        break;
                    case MultiAccountEvaluationStatus.AlreadyFlagged:
                        alreadyFlagged++;
                        break;
                    default:
                        noSignal++;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex,
                    "MultiAccountRetroScanJob failed to evaluate user {UserId} — continuing.",
                    userId);
            }
        }

        if (flagged > 0 || failed > 0)
        {
            _logger.LogInformation(
                "MultiAccountRetroScanJob scanned {Scanned} user(s): {Flagged} newly flagged, {AlreadyFlagged} already flagged, {NoSignal} clean, {Failed} failed.",
                candidateIds.Count, flagged, alreadyFlagged, noSignal, failed);
        }

        return new MultiAccountRetroScanOutcome(
            candidateIds.Count, flagged, alreadyFlagged, noSignal, failed);
    }
}

/// <summary>Aggregate counters for one retro-scan sweep (logging / tests).</summary>
public sealed record MultiAccountRetroScanOutcome(
    int Scanned,
    int Flagged,
    int AlreadyFlagged,
    int NoSignal,
    int Failed);
