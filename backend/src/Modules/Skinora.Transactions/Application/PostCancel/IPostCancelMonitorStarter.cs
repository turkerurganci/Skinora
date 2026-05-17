namespace Skinora.Transactions.Application.PostCancel;

/// <summary>
/// T75 ortak giriş noktası — bir transaction iptal/timeout sonucu terminal
/// duruma geçtiğinde post-cancel monitoring akışını başlatır.
/// <see cref="Skinora.Transactions.Application.Lifecycle.TransactionCancellationService"/>
/// (T51 kullanıcı iptali),
/// <see cref="Skinora.Transactions.Application.Timeouts.TimeoutExecutor"/>
/// (T49 timeout) ve
/// <see cref="Skinora.Transactions.Application.Admin.AdminTransactionService"/>
/// (T59 admin iptali) bu servisi çağırır.
/// </summary>
/// <remarks>
/// <para>
/// Implementation does <c>NOT</c> call <c>SaveChangesAsync</c> — the caller's
/// existing unit of work commits the state flip, the <c>PaymentAddress</c>
/// stamp and the
/// <see cref="Skinora.Shared.Events.PostCancelMonitorStartRequestedEvent"/>
/// outbox row in a single transaction (09 §13.3 atomicity).
/// </para>
/// <para>
/// Idempotent on <c>transactionId</c>: a missing <c>PaymentAddress</c>
/// (transaction cancelled before allocation) or one already in a
/// <c>POST_CANCEL_*</c> / <c>STOPPED</c> state is a no-op — the caller does
/// not need to know whether monitoring was actually started.
/// </para>
/// </remarks>
public interface IPostCancelMonitorStarter
{
    /// <summary>
    /// Mark the transaction's deposit address for post-cancel monitoring and
    /// queue the sidecar start event.
    /// </summary>
    /// <param name="transactionId">Transaction that just entered a cancel state.</param>
    /// <param name="cancelledAt">UTC moment the cancel transition fired —
    /// anchors the sidecar 24h/7d/30d windows.</param>
    /// <param name="cancellationToken"/>
    Task RequestStartAsync(
        Guid transactionId,
        DateTime cancelledAt,
        CancellationToken cancellationToken);
}
