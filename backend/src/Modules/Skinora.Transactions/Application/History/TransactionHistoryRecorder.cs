using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Transactions.Application.History;

/// <summary>
/// WP15 — shared, sync-inline recorder for the <see cref="TransactionHistory"/>
/// audit trail (06 §3.6 + 05 §5.4 — "her state geçişinin tam kaydı"). Every
/// state-transition caller invokes this immediately after a successful
/// <c>StateMachine.Fire()</c>, before its own <c>SaveChangesAsync</c>, so the
/// history row commits in the same unit of work as the status flip
/// (09 §13.3 atomicity boundary).
/// </summary>
/// <remarks>
/// <para>
/// A pure static helper (mirrors <c>WashTradingFilter.Apply</c>) rather than an
/// injectable service or a <c>SaveChanges</c> interceptor — the interceptor
/// route cannot supply the firing <c>Trigger</c> or the <c>Actor</c> (both are
/// application concerns the domain layer is forbidden from holding, 09 §9.2),
/// and per-caller injection would ripple a constructor parameter through every
/// transition service plus its direct-<c>new</c> tests. The caller already owns
/// an <see cref="AppDbContext"/>, so the row is added to its tracked context and
/// flushed by the caller's existing <c>SaveChangesAsync</c>.
/// </para>
/// <para>
/// The <c>TransactionHistory</c> entity is <see cref="Skinora.Shared.Domain.IAppendOnly"/>
/// (06 §4.2) — INSERT only; never updated or deleted.
/// </para>
/// </remarks>
public static class TransactionHistoryRecorder
{
    /// <summary>
    /// Records a state-machine transition. <paramref name="previousStatus"/> is
    /// the status captured BEFORE the <c>Fire()</c>; <c>NewStatus</c> is read
    /// from the now-transitioned <paramref name="transaction"/>.
    /// </summary>
    public static TransactionHistory Record(
        AppDbContext db,
        Transaction transaction,
        TransactionStatus previousStatus,
        TransactionTrigger trigger,
        ActorType actorType,
        Guid actorId,
        DateTime occurredAt,
        string? additionalData = null)
        => Record(db, transaction, previousStatus, trigger.ToString(), actorType, actorId, occurredAt, additionalData);

    /// <summary>
    /// Records a transition with an explicit trigger label. Used for the genesis
    /// row (creation, <paramref name="previousStatus"/> = <c>null</c> per 06 §3.6
    /// "ilk kayıtta null") where there is no <see cref="TransactionTrigger"/>.
    /// </summary>
    public static TransactionHistory Record(
        AppDbContext db,
        Transaction transaction,
        TransactionStatus? previousStatus,
        string trigger,
        ActorType actorType,
        Guid actorId,
        DateTime occurredAt,
        string? additionalData = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(transaction);

        var row = new TransactionHistory
        {
            // Id is long IDENTITY — DB-generated, left unset.
            TransactionId = transaction.Id,
            PreviousStatus = previousStatus,
            NewStatus = transaction.Status,
            Trigger = trigger,
            ActorType = actorType,
            ActorId = actorId,
            AdditionalData = additionalData,
            CreatedAt = occurredAt,
        };

        db.Set<TransactionHistory>().Add(row);
        return row;
    }

    /// <summary>
    /// Records the genesis row for a freshly created transaction:
    /// <c>PreviousStatus = null</c>, <c>NewStatus</c> = the created status
    /// (CREATED or FLAGGED), trigger label <c>"Create"</c> (06 §3.6).
    /// </summary>
    public const string GenesisTrigger = "Create";
}
