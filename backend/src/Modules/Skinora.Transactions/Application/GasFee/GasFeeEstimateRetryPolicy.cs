namespace Skinora.Transactions.Application.GasFee;

/// <summary>
/// T160b (öneriyle kaydedildi — ⚠ PARA) — how long a refund's gas estimate is
/// asked again after a retryable failure before the fallback is charged.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> T159b (B1): on both admin cancellations Ethereum's
/// service answered <c>502 FEE_ESTIMATE_SIMULATION_FAILED</c>
/// (<c>eth_estimateGas: block not found</c> — one of the provider's
/// load-balanced nodes a block behind) with <c>retryable: true</c>; the single
/// attempt fell back to the static 0.50 and that was deducted from the buyer,
/// while the send side waited for the same error and priced correctly a
/// moment later. The deduction is fixed once (08 §3.1a), so the moment it is
/// priced is the one chance to get it right.
/// </para>
/// <para>
/// <b>Why these numbers.</b> The estimate is a synchronous call on two
/// paths, neither with a short deadline of its own: the outbox dispatcher
/// (<c>PaymentRefundToBuyerConsumer</c> — one serial batch under the
/// <c>outbox-dispatcher</c> lock, no per-event timeout) and the blockchain
/// webhook (<c>AmountValidationService</c> — the services' webhook fetch
/// sets no timeout, Node's default answers after 300 s; no database
/// transaction is open while it waits). What a wait costs there is latency
/// for everything behind it (the next outbox events; on TRON the monitor
/// tick, which polls every address in one loop). A single attempt already
/// may take the estimate client's whole budget
/// (<c>BlockchainSidecarOptions.ResolveChainReadTimeout</c>, 30 s). So the
/// retries live inside <see cref="Budget"/> measured from the first call:
/// a retry starts only if it can start before the budget ends, and it is
/// cancelled when the budget ends. A first attempt that used up the budget
/// (a timeout) is not retried — the worst case stays one client timeout, as
/// before T160b; a fast failure (the T159b case answers in well under a
/// second) gets two more tries within 12 s. A lagging node catches up within
/// a block (12 s on Ethereum) and the next request usually reaches another
/// node; 1 s and 3 s apart covers both without holding the outbox long.
/// </para>
/// </remarks>
/// <param name="MaxAttempts">Calls in total, the first one included.</param>
/// <param name="Delays">Wait before the 2nd, 3rd, … call; the last entry repeats.</param>
/// <param name="Budget">Wall time from the first call after which nothing is retried.</param>
public sealed record GasFeeEstimateRetryPolicy(
    int MaxAttempts,
    IReadOnlyList<TimeSpan> Delays,
    TimeSpan Budget)
{
    /// <summary>3 calls, 1 s then 3 s apart, within 12 s (öneriyle kaydedildi — ⚠ PARA).</summary>
    public static readonly GasFeeEstimateRetryPolicy Default = new(
        MaxAttempts: 3,
        Delays: [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)],
        Budget: TimeSpan.FromSeconds(12));

    /// <summary>One call, no retry — the payout path (unchanged by T160b).</summary>
    public static readonly GasFeeEstimateRetryPolicy SingleAttempt = new(1, [], TimeSpan.Zero);

    /// <summary>The wait before call number <paramref name="attempt"/> (2-based).</summary>
    public TimeSpan DelayBefore(int attempt) =>
        Delays.Count == 0 ? TimeSpan.Zero : Delays[Math.Min(attempt - 2, Delays.Count - 1)];
}
