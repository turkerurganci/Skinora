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
/// <b>Reading.</b> The row is seeded <c>false</c> by the migration. Only a
/// row that reads exactly <c>false</c> means "send": a row that is present
/// and reads anything else (true, blank, unparsable) means paused — an
/// operator who tried to set it must not be ignored — and so does a
/// <b>missing</b> row (T172a doğrulama S4-2, öneriyle kaydedildi — ⚠ PARA).
/// A missing row is what a database restored from a backup taken before the
/// T172a migration looks like: the runbook's <c>UPDATE … = 'true'</c> then
/// touches 0 rows, and reading that as "not paused" would let the dispatcher
/// send the restored PENDING rows the moment a signing service is up. Every
/// migrated database has the row (seed + migration), so fail-closed costs
/// nothing in normal operation; a database that lost it stalls visibly (log
/// warning every run + the <c>outbound-dispatch-paused</c> alarm) until the
/// migrations are applied. The same rule as the approval gate
/// (<see cref="OutboundApprovalGate"/>: missing = closed).
/// The dispatcher reads it at the start of each run and again, straight from
/// the database, before each row it sends, so turning it on stops the very
/// next send of a run already under way.
/// </para>
/// </remarks>
public static class OutboundDispatchPause
{
    /// <summary>SystemSetting key: <c>true</c> = the dispatcher sends nothing.</summary>
    public const string PausedKey = "outbound.dispatch_paused";

    /// <summary>What <see cref="ReadAsync"/> found.</summary>
    /// <param name="Paused">true = the dispatcher sends nothing.</param>
    /// <param name="RowMissing">The <see cref="PausedKey"/> row does not exist (reads as paused).</param>
    /// <param name="RawValue">The stored value (null when missing or NULL).</param>
    public readonly record struct State(bool Paused, bool RowMissing, string? RawValue);

    public static async Task<bool> IsPausedAsync(AppDbContext db, CancellationToken cancellationToken) =>
        (await ReadAsync(db, cancellationToken)).Paused;

    public static async Task<State> ReadAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var row = await db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == PausedKey)
            .Select(s => new { s.Value })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) return new State(Paused: true, RowMissing: true, RawValue: null);
        var notPaused = bool.TryParse(row.Value?.Trim(), out var paused) && !paused;
        return new State(Paused: !notPaused, RowMissing: false, RawValue: row.Value);
    }
}
