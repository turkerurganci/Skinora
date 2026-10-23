using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// T172a — the operator's stop switch for the outgoing-transfer dispatcher
/// (DEPLOY_RUNBOOK §N.8). ⚠ PARA.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> After a database restore an outbound row that already went out
/// on chain can sit <c>PENDING</c> again. Proving that with the two-person
/// closure tool (AD33–AD37) needs the network's blockchain service running —
/// it reads the chain — but a running service lets the dispatcher sign every
/// approved payout/refund and every sweep at once. The approval gate
/// (<see cref="OutboundApprovalGate"/>) holds only unapproved payouts and
/// refunds; this switch holds everything the dispatcher sends, of every type,
/// on every network.
/// </para>
/// <para>
/// While <see cref="PausedKey"/> is <c>true</c> the dispatcher sends nothing:
/// rows stay <c>PENDING</c>, spend no retry and are not failed. Everything
/// else keeps running — the confirmation job (so a row the closure tool bound
/// to a hash is confirmed), deposit monitoring, sweep and payout queueing
/// (new rows wait too). Not covered: the admin's own hot → cold transfer
/// (AD39, a deliberate manual action).
/// </para>
/// <para>
/// <b>Reading.</b> The row is seeded <c>false</c> by the migration. A row that
/// is present and does not read exactly <c>false</c> (true, blank,
/// unparsable) means paused — an operator who tried to set it must not be
/// ignored. A missing row (impossible after the migration) means not paused.
/// The dispatcher reads it at the start of each run and again, straight from
/// the database, before each row it sends, so turning it on stops the very
/// next send of a run already under way.
/// </para>
/// </remarks>
public static class OutboundDispatchPause
{
    /// <summary>SystemSetting key: <c>true</c> = the dispatcher sends nothing.</summary>
    public const string PausedKey = "outbound.dispatch_paused";

    public static async Task<bool> IsPausedAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var row = await db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == PausedKey)
            .Select(s => new { s.Value })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) return false;
        return !(bool.TryParse(row.Value?.Trim(), out var paused) && !paused);
    }
}
