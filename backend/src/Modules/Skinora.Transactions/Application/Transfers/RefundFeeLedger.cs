using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// T160c — the books of the refund network fees withheld from buyers
/// (02 §4.7, 05 §3.3, 06 §3.8). Since T159a every refund sends
/// <c>base − gas</c> and records the withheld <c>gas</c> in
/// <see cref="BlockchainTransaction.GasFee"/>; the chain fee itself is paid by
/// the platform (TRX from the hot wallet, ETH from the fuel wallet). When the
/// refund leaves the deposit, the withheld amount stays there as tokens. This
/// is the one rule that says how much of it the deposit still holds for the
/// platform, shared by the two producers that move it
/// (<see cref="SweepQueueJob"/>, <see cref="RefundFeeSweepQueueJob"/>) and by
/// the admin wallet overview.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>Withheld</b> — a refund row (<see cref="RefundTypes"/>) that is
///   <c>CONFIRMED</c>, has a positive <c>GasFee</c> and left
///   <i>the deposit</i> (<c>FromAddress</c> = the deposit address). A refund
///   the hot wallet sent (Ethereum <c>BUYER_REFUND</c> after the sweep, T158
///   (f)) kept its fee in the hot wallet already; a refund not final yet still
///   holds its whole base on the deposit; a FAILED one moved nothing. The fee
///   is counted in the token the refund actually moved (a wrong-token refund
///   withholds USDC — T159a ⚠ 1).</item>
///   <item><b>Carried</b> — <see cref="BlockchainTransaction.RefundFeeRecovered"/>
///   of the transaction's <c>SWEEP</c> and <c>REFUND_FEE_SWEEP</c> rows that
///   are not <c>FAILED</c> (a queued or broadcast one counts: it is on its way
///   and must not be queued twice), per the token they move.</item>
///   <item><b>Uncollected</b> = withheld − carried, per token.</item>
/// </list>
/// Amounts come only from the ledger rows, never from an on-chain balance: a
/// refund the threshold blocked, a payment waiting for an admin decision or a
/// spam token on the same deposit is never part of it.
/// </remarks>
public static class RefundFeeLedger
{
    /// <summary>Refund types whose <c>GasFee</c> is withheld from the buyer (02 §4.6, §4.7).</summary>
    public static readonly BlockchainTransactionType[] RefundTypes =
    [
        BlockchainTransactionType.BUYER_REFUND,
        BlockchainTransactionType.EXCESS_REFUND,
        BlockchainTransactionType.WRONG_TOKEN_REFUND,
        BlockchainTransactionType.INCORRECT_AMOUNT_REFUND,
        BlockchainTransactionType.LATE_PAYMENT_REFUND,
    ];

    /// <summary>Deposit → hot wallet types that may carry withheld refund fees.</summary>
    public static readonly BlockchainTransactionType[] CollectionTypes =
    [
        BlockchainTransactionType.SWEEP,
        BlockchainTransactionType.REFUND_FEE_SWEEP,
    ];

    /// <summary>True when <paramref name="row"/> withheld a fee that stayed on <paramref name="depositAddress"/>.</summary>
    public static bool IsWithheldOnDeposit(BlockchainTransaction row, string depositAddress) =>
        RefundTypes.Contains(row.Type)
        && row.Status == BlockchainTransactionStatus.CONFIRMED
        && row.GasFee is > 0m
        && BlockchainAddress.AreEqual(row.FromAddress, depositAddress);

    /// <summary>True when <paramref name="row"/> carries (or is carrying) withheld fees to the hot wallet.</summary>
    public static bool IsCarrying(BlockchainTransaction row) =>
        CollectionTypes.Contains(row.Type)
        && row.Status != BlockchainTransactionStatus.FAILED
        && row.RefundFeeRecovered is > 0m;

    /// <summary>
    /// Withheld − carried per token, for one transaction's rows. Tokens with
    /// nothing left are omitted. <paramref name="chainToken"/> names the token
    /// a refund row actually moved (<see cref="OutboundTransferExpectation.ChainToken(BlockchainTransaction, Networks.IStablecoinContractRegistry)"/>);
    /// <c>null</c> = not resolvable, the row is left out.
    /// </summary>
    public static IReadOnlyDictionary<StablecoinType, decimal> Uncollected(
        IEnumerable<BlockchainTransaction> rows,
        string depositAddress,
        Func<BlockchainTransaction, StablecoinType?> chainToken)
    {
        var balance = new Dictionary<StablecoinType, decimal>();
        foreach (var row in rows)
        {
            if (IsWithheldOnDeposit(row, depositAddress))
            {
                if (chainToken(row) is not { } token) continue;
                balance[token] = balance.GetValueOrDefault(token) + row.GasFee!.Value;
            }
            else if (IsCarrying(row))
            {
                balance[row.Token] = balance.GetValueOrDefault(row.Token) - row.RefundFeeRecovered!.Value;
            }
        }
        return balance
            .Where(kv => kv.Value > 0m)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// What <see cref="SweepQueueJob"/> adds to the sweep: the uncollected fee
    /// in the transaction's own token. A wrong-token refund is left out — it
    /// withheld another token, which this sweep does not move.
    /// </summary>
    public static async Task<decimal> UncollectedAsync(
        AppDbContext db,
        Guid transactionId,
        string depositAddress,
        StablecoinType token,
        CancellationToken cancellationToken)
    {
        var rows = await LoadAsync(db, transactionId, cancellationToken);
        var uncollected = Uncollected(
            rows,
            depositAddress,
            r => r.Type == BlockchainTransactionType.WRONG_TOKEN_REFUND ? null : r.Token);
        return uncollected.GetValueOrDefault(token);
    }

    /// <summary>The transaction's refund and collection rows (soft-delete filter ignored — money rows).</summary>
    public static Task<List<BlockchainTransaction>> LoadAsync(
        AppDbContext db, Guid transactionId, CancellationToken cancellationToken) =>
        db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(b => b.TransactionId == transactionId
                && (RefundTypes.Contains(b.Type) || CollectionTypes.Contains(b.Type)))
            .ToListAsync(cancellationToken);
}
