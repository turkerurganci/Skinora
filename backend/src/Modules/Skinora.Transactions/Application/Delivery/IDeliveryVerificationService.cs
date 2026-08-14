using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// T125 — the 02 §9.2 delivery evidence engine.
/// </summary>
/// <remarks>
/// <para>
/// <b>Side-effect free by contract.</b> A round reads (the two Steam
/// inventories, the transaction, one SystemSetting) and returns a verdict. It
/// writes nothing: no entity mutation, no <c>SaveChanges</c>, no state-machine
/// trigger, no outbox message. Callers own every consequence.
/// </para>
/// <para>
/// That is what makes it safe to poll. 02 §9.2 requires the rules to run at
/// three separate moments — buyer confirmation, dispute open, and just before
/// the delivery timeout — and a future job may run them on a schedule. An
/// engine that mutated on observation would produce different answers depending
/// on how often it had been called.
/// </para>
/// <para>
/// <b>Callers:</b> T126 (<c>POST /transactions/:id/confirm-receipt</c>), T127
/// (the scanner's pre-timeout verification round) and T130 (dispute
/// auto-check). None exist yet — this task delivers the engine and its
/// evidence rules only.
/// </para>
/// <para>
/// <b>Caller invariant — do not stamp <c>DeliveryVerifiedAt</c> on a gated
/// round.</b> The launch gate (DEPLOY_RUNBOOK §H) lives here, in the service.
/// The state machine does not know about it: <c>HasDeliveryEvidence()</c>
/// guards <c>DeliverItem</c> on
/// <c>DeliveryEvidence.IsSufficientForDelivery() &amp;&amp;
/// DeliveryVerifiedAt.HasValue</c> alone. So a caller that persists
/// <see cref="DeliveryVerificationResult.Evidence"/> <em>and</em> stamps
/// <c>DeliveryVerifiedAt</c> while
/// <see cref="DeliveryVerificationResult.AutoReleaseGated"/> is <c>true</c>
/// silently opens the gate — the inventory inference would release money
/// before a human ever read the captured evidence.
/// <c>DeliveryVerifiedAt</c> is the field that actually holds the gate shut.
/// Persisting the evidence flags on a gated round is fine and intended; only
/// the timestamp must wait. (Buyer confirmation is unaffected:
/// <c>AutoReleaseGated</c> is always <c>false</c> there.)
/// </para>
/// </remarks>
public interface IDeliveryVerificationService
{
    /// <summary>
    /// Run one verification round for <paramref name="transaction"/>.
    /// </summary>
    /// <param name="transaction">
    /// The transaction to verify. It is read, never modified.
    /// </param>
    /// <param name="freshness">
    /// Whether the Steam reads may be served from the sidecar's 120-second
    /// cache. Callers that will act on the answer pass
    /// <see cref="InventoryReadFreshness.Fresh"/> — 02 §10.1 requires the
    /// dispute path to re-run the rules "taze olarak", and a cached read can
    /// still show an item the seller traded away two minutes ago.
    /// </param>
    Task<DeliveryVerificationResult> VerifyAsync(
        Transaction transaction,
        InventoryReadFreshness freshness,
        CancellationToken cancellationToken);
}
