using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Steam.Domain.Entities;

/// <summary>
/// A single entry of the S18 Recovery Queue (T103b-2 — 02 §15, 03 §11.2a,
/// 04 §8.7). One row is materialised — eagerly, when a bot transitions into
/// RESTRICTED/BANNED — for every transaction whose item is still in that bot's
/// custody and therefore cannot continue automatically.
/// </summary>
/// <remarks>
/// <para>
/// The row carries only the admin-managed triage state plus the immutable
/// <see cref="StatusAtRestriction"/> snapshot ("İşlem State — kısıtlama öncesi").
/// Display data the queue needs (item name, parties, current status) is joined
/// from the <c>Transaction</c> at read time rather than duplicated here, so it
/// never drifts.
/// </para>
/// <para>
/// One recovery row per transaction is enforced by a unique index on
/// <see cref="TransactionId"/>, which also makes materialisation idempotent
/// (a bot flipping restricted→…→restricted re-runs the consumer harmlessly).
/// Mutable + audited (<see cref="IAuditableEntity"/>); NOT append-only — admins
/// update status / note / responsible admin as triage proceeds.
/// </para>
/// </remarks>
public class BotRecoveryItem : BaseEntity, IAuditableEntity
{
    /// <summary>The restricted/banned bot holding the item.</summary>
    public Guid PlatformSteamBotId { get; set; }

    /// <summary>The stuck transaction (unique — one recovery row per transaction).</summary>
    public Guid TransactionId { get; set; }

    /// <summary>Admin triage state (Bekliyor / İnceleniyor / Çözüldü).</summary>
    public BotRecoveryStatus RecoveryStatus { get; set; }

    /// <summary>Transaction status captured at materialisation (kısıtlama öncesi state).</summary>
    public TransactionStatus StatusAtRestriction { get; set; }

    /// <summary>Admin assigned to drive recovery (optional). FK → User.</summary>
    public Guid? ResponsibleAdminId { get; set; }

    /// <summary>Free-text admin investigation notes (optional).</summary>
    public string? AdminNote { get; set; }

    /// <summary>When <see cref="RecoveryStatus"/> reached RESOLVED.</summary>
    public DateTime? ResolvedAt { get; set; }
}
