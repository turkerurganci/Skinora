namespace Skinora.Users.Application.Reputation;

/// <summary>
/// Implements the 02 §14.1 / §13 wash-trading rule for the
/// <see cref="ReputationAggregator"/> denominator: within an unordered
/// (sellerId, buyerId) pair, transactions less than 1 month apart from the
/// last *counted* transaction in that pair are dropped from
/// <c>SuccessfulTransactionRate</c>.
/// </summary>
/// <remarks>
/// <para>
/// "Skor etkisi kaldırılır" (02 §14.1) means the filtered transaction
/// contributes neither to the numerator nor the denominator. The first
/// transaction in any pair is always counted; each subsequent transaction is
/// counted only if it is at least
/// <see cref="WashTradingWindow"/> after the previously counted one in the
/// same pair.
/// </para>
/// <para>
/// <b>Not applied</b> to <c>CompletedTransactionCount</c> — that field is the
/// raw count of completed transactions (06 §3.1 + 02 §13 leave the wash
/// filter scoped to the rate denominator).
/// </para>
/// </remarks>
public static class WashTradingFilter
{
    /// <summary>1 month per 02 §14.1 — interpreted as 30 days for stable arithmetic.</summary>
    public static readonly TimeSpan WashTradingWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// Tag each input row as counted or filtered, in chronological order per pair.
    /// </summary>
    /// <param name="rows">Transactions involving the target user — must contain the unordered pair (sellerId, buyerId).</param>
    /// <returns>Same rows in input order, each annotated with <see cref="WashTradingResult.Counted"/>.</returns>
    public static IReadOnlyList<WashTradingResult<T>> Apply<T>(
        IEnumerable<T> rows,
        Func<T, (Guid A, Guid B)> pairSelector,
        Func<T, DateTime> timestampSelector)
    {
        var sorted = rows
            .Select((row, index) => (row, index, ts: timestampSelector(row)))
            .OrderBy(t => t.ts)
            .ToList();

        var lastCountedByPair = new Dictionary<(Guid, Guid), DateTime>();
        var verdicts = new WashTradingResult<T>[sorted.Count];

        foreach (var (row, originalIndex, ts) in sorted)
        {
            var pair = NormalizePair(pairSelector(row));
            var counted = true;

            if (lastCountedByPair.TryGetValue(pair, out var lastTs))
            {
                if (ts - lastTs < WashTradingWindow)
                    counted = false;
            }

            if (counted)
                lastCountedByPair[pair] = ts;

            verdicts[originalIndex] = new WashTradingResult<T>(row, counted);
        }

        return verdicts;
    }

    private static (Guid, Guid) NormalizePair((Guid A, Guid B) pair) =>
        pair.A.CompareTo(pair.B) <= 0 ? (pair.A, pair.B) : (pair.B, pair.A);
}

/// <summary>Verdict for a single row from <see cref="WashTradingFilter.Apply"/>.</summary>
public readonly record struct WashTradingResult<T>(T Row, bool Counted);
