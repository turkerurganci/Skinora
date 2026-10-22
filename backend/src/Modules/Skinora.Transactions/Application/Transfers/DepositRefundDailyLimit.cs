using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// T169a — the per-network limit on refunds that leave a DEPOSIT address
/// (proje sahibi kararı 2026-10-21 — ⚠ PARA; 08 §3.3c, DEFERRED_BACKLOG
/// <c>DepositRefundDailyLimitUndecided</c>). The signing services cap the hot
/// wallet's 24-hour outflow, but a refund leaves the deposit it was paid into,
/// so nothing capped how much refunds could send in a day. Now: the refunds a
/// network sends from deposits in any 24 hours may not exceed
/// <c>blockchain.*.deposit_refund_daily_limit_usdt</c> (seed 100). A refund
/// that would cross it waits (PENDING, no retry spent, never FAILED), the
/// alarm fires, and it goes out when the window has room again or when an
/// admin releases it (AD42).
/// </summary>
/// <remarks>
/// <para>
/// <b>What counts.</b> Every refund type (<see cref="RefundTypes"/>) the
/// dispatcher sends from a deposit — TRON always, Ethereum before the sweep
/// (<see cref="RefundSourceResolver"/>). Hot-wallet refunds, payouts, sweeps
/// and fee collections do not. A refund is counted when the dispatcher stamps
/// <c>DepositRefundCountedAt</c> just before the broadcast; the window's total is
/// the <c>Amount</c> (what the buyer receives) of every non-FAILED row stamped in
/// the last 24 hours, the row being decided excluded (a retry does not count
/// itself twice). USDC (a wrong-token refund) counts 1:1.
/// </para>
/// <para>
/// <b>Rolling 24 hours</b> (öneriyle kaydedildi — ⚠ PARA), as the services'
/// hot-wallet daily cap (<c>OUTFLOW_WINDOW_MS</c>): no window edge where twice
/// the limit could go out within minutes. "Next day" = when the refunds counted
/// 24 hours earlier leave the window.
/// </para>
/// <para>
/// <b>Concurrency.</b> The check and the stamp run in one database transaction
/// holding the network's row of <see cref="DepositRefundLimitLock"/> exclusively
/// (an UPDATE, released at commit), so two dispatchers — two ticks, two hosts —
/// can never both read the same total and both send; the stamp is committed before the broadcast,
/// so a refund in flight already counts for the next one. The dispatcher's own
/// <c>[DisableConcurrentExecution]</c> stays the first layer.
/// </para>
/// <para>
/// <b>Admin.</b> The approval gate (T169) is checked first: a row that still
/// needs an approval is never weighed against the limit. An approved refund
/// that would cross the limit waits like any other; approval does not lift it.
/// Only the separate, audited release (AD42, <c>DepositRefundLimitReleasedAt</c>)
/// lets one held row go past the limit; it still counts toward the window
/// (öneriyle kaydedildi — ⚠ PARA). A refund larger than the limit itself never
/// passes on its own — it always needs the release.
/// </para>
/// </remarks>
public static class DepositRefundDailyLimit
{
    /// <summary>The window the limit is measured over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>
    /// How soon a held refund is weighed again. The window frees room only
    /// when an earlier refund turns 24 hours old, so a minute's cadence would
    /// only repeat the same answer; a quarter of an hour bounds the delay
    /// after the room appears.
    /// </summary>
    public static readonly TimeSpan HeldRetryDelay = TimeSpan.FromMinutes(15);

    /// <summary>The <c>ErrorMessage</c> code a held row carries.</summary>
    public const string HeldErrorCode = "DEPOSIT_REFUND_DAILY_LIMIT";

    /// <summary>The refund family the limit applies to (when it leaves a deposit).</summary>
    public static readonly BlockchainTransactionType[] RefundTypes =
    [
        BlockchainTransactionType.BUYER_REFUND,
        BlockchainTransactionType.EXCESS_REFUND,
        BlockchainTransactionType.WRONG_TOKEN_REFUND,
        BlockchainTransactionType.INCORRECT_AMOUNT_REFUND,
        BlockchainTransactionType.LATE_PAYMENT_REFUND,
    ];

    /// <summary>Whether <paramref name="type"/> is a refund the limit weighs (when it leaves a deposit).</summary>
    public static bool Applies(BlockchainTransactionType type) => RefundTypes.Contains(type);

    /// <summary>
    /// A row the limit is holding right now: PENDING, held at least once, not
    /// counted since (counting happens only when it passes).
    /// </summary>
    public static readonly Expression<Func<BlockchainTransaction, bool>> IsHeld = b =>
        b.Status == BlockchainTransactionStatus.PENDING
        && b.DepositRefundLimitHeldAt != null
        && b.DepositRefundCountedAt == null;

    /// <summary>
    /// What the deposit refunds of <paramref name="network"/> counted in the 24
    /// hours before <paramref name="now"/> sent (FAILED rows excluded — nothing
    /// left), without <paramref name="excludingId"/>. Summed in memory: a day
    /// holds a handful of rows, and SQLite cannot sum decimals in SQL.
    /// </summary>
    public static async Task<decimal> UsedAsync(
        AppDbContext db,
        BlockchainNetwork network,
        DateTime now,
        Guid? excludingId,
        CancellationToken cancellationToken)
    {
        var since = now - Window;
        var amounts = await db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(b => b.Network == network
                && RefundTypes.Contains(b.Type)
                && b.DepositRefundCountedAt != null
                && b.DepositRefundCountedAt > since
                && b.Status != BlockchainTransactionStatus.FAILED
                && (excludingId == null || b.Id != excludingId))
            .Select(b => b.Amount)
            .ToListAsync(cancellationToken);
        return amounts.Sum();
    }

    /// <summary>
    /// Takes the network's lock for the check-and-stamp: an UPDATE of its
    /// <see cref="DepositRefundLimitLock"/> row inside the caller's transaction.
    /// The exclusive row lock lasts until that transaction ends, so a second
    /// dispatcher of the same network waits here (other networks do not). On
    /// SQLite (unit tests) one connection serialises everything anyway.
    /// </summary>
    public static async Task LockAsync(
        AppDbContext db, BlockchainNetwork network, DateTime now, CancellationToken cancellationToken)
    {
        var updated = await db.Set<DepositRefundLimitLock>()
            .Where(l => l.Network == network)
            .ExecuteUpdateAsync(u => u.SetProperty(l => l.LockedAt, now), cancellationToken);
        if (updated != 1)
        {
            // The rows are seeded by the T169a migration; without one there is
            // nothing to lock, and an unlocked check could let two refunds read
            // the same total — so nothing is sent.
            throw new InvalidOperationException(
                $"DepositRefundLimitLocks has no row for {network} — the deposit refund daily limit cannot be checked safely.");
        }
    }
}
