using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Domain.Entities;

/// <summary>
/// A two-person request to close one outbound row with the hash of the
/// transaction that is already on chain (T157 — 06 §3.8b, 07 §9.32,
/// DEPLOY_RUNBOOK §D adım 7.4; T172a — TRON, and the post-restore FAILED
/// row, DEPLOY_RUNBOOK §N.8). ⚠ PARA.
/// </summary>
/// <remarks>
/// <para>
/// Why it exists: when the Ethereum service loses its transaction journal, it
/// stops signing (<c>JOURNAL_BEHIND_CHAIN</c>) because a row it no longer
/// remembers would be signed a second time — a double payout. The operator
/// finds the nonces in between on the chain and matches each to a Skinora row.
/// That match is this record: it binds the row to the on-chain hash so the
/// dispatcher never sends the row again and the confirmation job finishes it.
/// </para>
/// <para>
/// The tool never sends money. Approval writes <see cref="TxHash"/> to the
/// row and moves it <c>PENDING → DETECTED</c> (or, T172a, a <c>FAILED</c> row
/// noted <c>POST_RESTORE_DUPLICATE</c> → <c>DETECTED</c>); the sender is
/// written when the row recorded none — nothing else.
/// </para>
/// <para>
/// Two admins: <see cref="ProposedByAdminId"/> proposes, a different admin
/// approves (<see cref="DecidedByAdminId"/> ≠ proposer, enforced by a CHECK as
/// well as by the service). A rejection may come from anyone authorised,
/// including the proposer withdrawing it.
/// </para>
/// </remarks>
public class OutboundRowClosure : BaseEntity, IAuditableEntity
{
    /// <summary>The <c>BlockchainTransaction</c> row to be closed.</summary>
    public Guid BlockchainTransactionId { get; set; }

    /// <summary>The row's network — TRON or Ethereum (CHECK; T172a added TRON).</summary>
    public BlockchainNetwork Network { get; set; } = BlockchainNetwork.ETHEREUM;

    /// <summary>
    /// The on-chain hash the row is closed with, lowercase: Ethereum <c>0x</c> +
    /// 64 hex, TRON 64 hex without a prefix.
    /// </summary>
    public string TxHash { get; set; } = string.Empty;

    /// <summary>Why this hash belongs to this row (what the proposer found on the chain).</summary>
    public string Reason { get; set; } = string.Empty;

    public OutboundRowClosureStatus Status { get; set; } = OutboundRowClosureStatus.PROPOSED;

    public Guid ProposedByAdminId { get; set; }

    /// <summary>The approver, or whoever rejected/withdrew; NULL while <c>PROPOSED</c>.</summary>
    public Guid? DecidedByAdminId { get; set; }

    public DateTime? DecidedAt { get; set; }

    /// <summary>Free-text note on the decision (rejection reason).</summary>
    public string? DecisionNote { get; set; }

    // --- Navigation ---
    public BlockchainTransaction BlockchainTransaction { get; set; } = null!;
}
