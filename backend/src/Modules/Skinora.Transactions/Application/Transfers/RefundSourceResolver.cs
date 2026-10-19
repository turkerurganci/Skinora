using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>Where a refund leaves from (T158 (f) — 08 §3.7.6 "Kaynaklar").</summary>
public enum RefundSource
{
    /// <summary>The deposit address the money arrived on (before the sweep; every TRON refund).</summary>
    Deposit,

    /// <summary>The network's hot wallet — the deposit was swept, the escrowed money is there.</summary>
    HotWallet,

    /// <summary>
    /// The deposit's sweep is in flight (queued, signed or broadcast, not yet
    /// final): the money is leaving or may already have left the deposit. The
    /// refund waits — no retry spent, not FAILED — until the sweep settles:
    /// confirmed → hot wallet, failed → deposit.
    /// </summary>
    WaitForSweep,
}

/// <summary>
/// T158 (f) — decides the source of a refund from the transaction's SWEEP row
/// (08 §3.7.6 "Kaynaklar": İade sweep öncesi depozitten, sonrası sıcak
/// cüzdandan). Shared by the refund consumer (which prices the transfer from
/// that sender) and the dispatcher (which sends it), so the two never disagree.
/// </summary>
/// <remarks>
/// <para>
/// Only on a network whose service signs hot-wallet refunds
/// (<see cref="BlockchainNetworkPolicy.RefundsAfterSweepFromHotWallet"/> —
/// Ethereum) and only for <see cref="BlockchainTransactionType.BUYER_REFUND"/>:
/// the sweep moves the escrowed total (<c>Transaction.TotalAmount</c>, in the
/// expected token), which is exactly what a buyer refund returns. Every other
/// refund type stays on the deposit: an excess, a wrong token or a late
/// payment is never part of the sweep and is still on the deposit after it.
/// TRON keeps today's deposit-only behaviour.
/// </para>
/// <para>
/// A SWEEP row that is <c>PENDING</c> may already be signed on Ethereum (the
/// service journals before it answers, and a timeout keeps the row PENDING —
/// T152 (xvii)); <c>DETECTED</c> is broadcast, not final. In both the refund
/// waits: sent from the deposit it would race the sweep for the same balance,
/// sent from the hot wallet it could pay out money the sweep has not delivered
/// (a reverted sweep leaves it on the deposit). Only a <c>CONFIRMED</c> sweep
/// moves the source to the hot wallet; a <c>FAILED</c> one leaves it on the
/// deposit (öneriyle kaydedildi — ⚠ PARA, 11 §F8 "T158 kararları").
/// </para>
/// </remarks>
public static class RefundSourceResolver
{
    public static async Task<RefundSource> ResolveAsync(
        AppDbContext db,
        BlockchainNetwork network,
        BlockchainTransactionType refundType,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        if (!BlockchainNetworkPolicy.For(network).RefundsAfterSweepFromHotWallet
            || refundType != BlockchainTransactionType.BUYER_REFUND)
        {
            return RefundSource.Deposit;
        }

        var sweepStatuses = await db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(b => b.TransactionId == transactionId
                && b.Type == BlockchainTransactionType.SWEEP
                && b.Network == network)
            .Select(b => b.Status)
            .ToListAsync(cancellationToken);

        return Decide(sweepStatuses);
    }

    /// <summary>The rule on the transaction's SWEEP row statuses (pure; one row in practice — UQ per transaction).</summary>
    public static RefundSource Decide(IReadOnlyCollection<BlockchainTransactionStatus> sweepStatuses)
    {
        if (sweepStatuses.Contains(BlockchainTransactionStatus.CONFIRMED)) return RefundSource.HotWallet;
        if (sweepStatuses.Any(s => s is BlockchainTransactionStatus.PENDING or BlockchainTransactionStatus.DETECTED))
        {
            return RefundSource.WaitForSweep;
        }
        return RefundSource.Deposit;
    }
}
