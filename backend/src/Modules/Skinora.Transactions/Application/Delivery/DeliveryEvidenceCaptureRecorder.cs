using System.Text.Json;
using System.Text.Json.Serialization;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// T125 — writes the launch-gate audit row for one verification round
/// (DEPLOY_RUNBOOK §H).
/// </summary>
/// <remarks>
/// <para>
/// A static helper over the caller's <see cref="AppDbContext"/>, mirroring
/// <c>TransactionHistoryRecorder</c>: the row is added to the caller's tracked
/// context and lands in the caller's existing <c>SaveChangesAsync</c>, so the
/// capture and the decision it justifies commit or roll back together.
/// </para>
/// <para>
/// Deliberately NOT folded into <see cref="DeliveryVerificationService"/>. That
/// service is side-effect free by contract so it can be polled; a write hidden
/// inside it would make every observation change the database and turn a
/// re-run into a new row. Keeping the write here also keeps it visible at the
/// call site — the caller decides that this round was worth recording.
/// </para>
/// <para>
/// <b>Callers arrive with T126 / T127.</b> This task delivers the mechanism and
/// the gate it feeds; the confirm-receipt endpoint and the scanner's
/// verification round are where rounds are actually run in production.
/// </para>
/// </remarks>
public static class DeliveryEvidenceCaptureRecorder
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Record <paramref name="result"/> if it carries a capture, otherwise do
    /// nothing and return <c>null</c>.
    /// </summary>
    /// <remarks>
    /// The no-op return is intentional so callers can invoke this
    /// unconditionally: a round that found no movement is not evidence about
    /// anything, and capturing every poll would bury the rows a reviewer needs
    /// (<c>DeliveryVerificationService.BuildCapture</c> decides which rounds
    /// qualify).
    /// </remarks>
    public static DeliveryEvidenceCapture? Record(
        AppDbContext db,
        Transaction transaction,
        DeliveryVerificationResult result,
        DateTime recordedAt)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Capture is null) return null;

        var row = new DeliveryEvidenceCapture
        {
            // Id is long IDENTITY — DB-generated, left unset.
            TransactionId = transaction.Id,
            ObservedAt = result.Capture.ObservedAt,
            Verdict = result.Verdict.ToString(),
            Evidence = result.Evidence,
            AutoReleaseGated = result.AutoReleaseGated,
            Payload = JsonSerializer.Serialize(result.Capture, PayloadOptions),
            CreatedAt = recordedAt,
        };

        db.Set<DeliveryEvidenceCapture>().Add(row);
        return row;
    }
}
