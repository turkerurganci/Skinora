using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Domain.Entities;

/// <summary>
/// T125 — one recorded delivery-evidence observation, kept for the launch gate
/// (DEPLOY_RUNBOOK §H).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this table exists.</b> T122 could not measure three things without a
/// real trade between two Steam accounts (runbook §7): how long a delivery
/// takes to appear, whether <c>assetid</c> really rotates, and whether an
/// <c>Item Certificate</c> survives the trade. The owner's decision was to
/// measure them from production instead of from a manual spike — the first real
/// deliveries record what the platform saw, a human reads those rows, and only
/// then is automatic money release on inventory evidence switched on.
/// </para>
/// <para>
/// <b>Append-only</b> (06 §4.2, <see cref="IAppendOnly"/>): the rows are the
/// evidence behind a decision about someone's money. A capture that could be
/// edited after the fact would be worth nothing to the reviewer it exists for.
/// </para>
/// <para>
/// Scope is the transaction's own item class on both sides — not an inventory
/// dump. Third-party inventory contents are personal data (T122 runbook §8),
/// and the questions the capture answers are all about this one item.
/// </para>
/// </remarks>
public class DeliveryEvidenceCapture : IAppendOnly
{
    public long Id { get; set; }

    public Guid TransactionId { get; set; }

    /// <summary>When the observation was made (not when the row was written).</summary>
    public DateTime ObservedAt { get; set; }

    /// <summary>
    /// The <c>DeliveryVerdict</c> this round reached, stored by name. A string
    /// rather than an int: these rows outlive the enum's ordering and are read
    /// by a human in a SQL client.
    /// </summary>
    public string Verdict { get; set; } = string.Empty;

    /// <summary>
    /// The cumulative evidence after this round — the value a caller would
    /// persist onto <c>Transaction.DeliveryEvidence</c> (06 §2.24).
    /// </summary>
    public DeliveryEvidence Evidence { get; set; }

    /// <summary>
    /// Whether the launch gate was closed at observation time, i.e. whether this
    /// row is one of the ones being collected FOR the review.
    /// </summary>
    public bool AutoReleaseGated { get; set; }

    /// <summary>
    /// The full observation as JSON (<c>DeliveryEvidenceCaptureData</c>):
    /// visibilities, baseline vs observed counts, asset IDs, per-asset
    /// properties, and the timestamps needed to derive delivery latency.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
