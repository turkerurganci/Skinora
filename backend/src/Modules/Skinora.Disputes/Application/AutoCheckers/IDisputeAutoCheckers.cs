using Skinora.Transactions.Domain.Entities;

namespace Skinora.Disputes.Application.AutoCheckers;

/// <summary>
/// Outcome of a single auto-check pass run by one of the type-specific
/// dispute auto-checkers (T58 — 02 §10.1, 03 §6.1–§6.3).
/// </summary>
/// <remarks>
/// The dispute service consumes this struct verbatim:
/// <list type="bullet">
///   <item>
///     <see cref="Resolved"/> = <c>true</c> closes the dispute on the spot
///     (status → CLOSED, ResolvedAt set, buyer notified via <c>DISPUTE_RESULT</c>).
///   </item>
///   <item>
///     <see cref="AutoEscalated"/> = <c>true</c> short-circuits to the admin
///     queue (status → ESCALATED, both parties notified). Currently only the
///     WRONG_ITEM checker emits this when on-platform / delivered class IDs
///     diverge (03 §6.3 step 5).
///   </item>
///   <item>
///     Both <c>false</c> leaves the dispute OPEN; the buyer may then submit a
///     TX hash (PAYMENT) or escalate manually (any type).
///   </item>
/// </list>
/// <see cref="MessageKey"/> is a stable key (see <see cref="DisputeAutoCheckMessages"/>),
/// localized to the buyer's locale by the dispute service before it reaches the
/// 07 §7.8 <c>autoCheckResult.message</c> field (WP17). <see cref="CanSubmitTxHash"/>
/// and <see cref="CanEscalate"/> mirror the <c>autoCheckResult</c> shape — surfaced
/// verbatim on the open response.
/// </remarks>
public sealed record AutoCheckResult(
    bool Resolved,
    bool AutoEscalated,
    string MessageKey,
    bool CanSubmitTxHash,
    bool CanEscalate)
{
    /// <summary>
    /// T130 — the Steam name of the item that actually arrived, on the WRONG_ITEM
    /// mismatch branch only (02 §10.1: "gelen item'ın adı kayda geçirilerek
    /// admin'e yükseltilir"). Copied verbatim onto
    /// <c>Dispute.DeliveredItemName</c> by the dispute service.
    /// </summary>
    /// <remarks>
    /// An init-only member rather than a sixth positional parameter: it is
    /// evidence one branch of one checker produces, and forcing every other
    /// construction site to state <c>null</c> would misrepresent it as part of
    /// the shape every auto-check answers in.
    /// </remarks>
    public string? DeliveredItemName { get; init; }
}

/// <summary>
/// Read port over the buyer's blockchain payment trail (02 §10.1 first row,
/// 03 §6.1). Backed by the <c>BlockchainTransactions</c> table the
/// <c>TransactionMonitor</c> sidecar (T71) writes to.
/// </summary>
public interface IPaymentDisputeAutoChecker
{
    /// <summary>
    /// Run the on-open auto-check: did the platform already detect a confirmed
    /// BUYER_PAYMENT row for this transaction?
    /// </summary>
    Task<AutoCheckResult> CheckAsync(Transaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Re-run the auto-check using a buyer-supplied transaction hash
    /// (07 §7.9). Implementations may persist the hash for the sidecar to
    /// reconcile asynchronously when the on-chain query returns nothing.
    /// </summary>
    Task<AutoCheckResult> CheckWithTxHashAsync(
        Transaction transaction,
        string txHash,
        CancellationToken cancellationToken);
}

/// <summary>
/// The 02 §10.1 second row / 03 §6.2 delivery check. v3.0 — there is no
/// <c>TradeOffers</c> table behind this any more: the platform is not a party to
/// the seller→buyer trade, so delivery is established by re-running the 02 §9.2
/// evidence rules against both inventories.
/// </summary>
/// <remarks>
/// T130 — no longer a pure read. The rules are run <em>fresh</em> through
/// <see cref="Skinora.Transactions.Application.Delivery.IDeliveryDisputeRound"/>,
/// which may advance the transaction to <c>ITEM_DELIVERED</c> (03 §6.2 Sonuç A)
/// on the caller's unit of work.
/// </remarks>
public interface IDeliveryDisputeAutoChecker
{
    Task<AutoCheckResult> CheckAsync(Transaction transaction, CancellationToken cancellationToken);
}

/// <summary>
/// Read port over what actually arrived in the buyer's inventory
/// (02 §10.1 third row, 03 §6.3). Diffs the buyer's current inventory against
/// the 06 §3.5 <c>BuyerBaselineClassIds</c> fingerprint and compares whatever
/// arrived against <see cref="Transaction.ItemClassId"/>.
/// </summary>
public interface IWrongItemDisputeAutoChecker
{
    Task<AutoCheckResult> CheckAsync(Transaction transaction, CancellationToken cancellationToken);
}
