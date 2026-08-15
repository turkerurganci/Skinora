using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Delivery;

// ---------- POST /transactions/:id/confirm-receipt (07 §7.6b) ----------

/// <summary>
/// Response body for <c>POST /transactions/:id/confirm-receipt</c> (07 §7.6b).
/// </summary>
/// <param name="Evidence">
/// The 02 §9.2 evidence flags recorded on the transaction, expanded into names.
/// A buyer-driven confirmation always contains <c>BUYER_CONFIRMED</c>; the
/// inventory flags appear alongside it when an earlier round already observed
/// them. Names rather than the raw flags integer because the column outlives the
/// enum's ordinal layout and this list is read by humans (06 §8.4).
/// </param>
public sealed record ConfirmReceiptResponse(
    TransactionStatus Status,
    DateTime DeliveryVerifiedAt,
    IReadOnlyList<string> Evidence);

/// <summary>
/// Outcome of <see cref="IDeliveryConfirmationService.ConfirmReceiptAsync"/>.
/// The controller pattern-matches on <see cref="Status"/> to produce 200 / 4xx
/// responses without leaking implementation details.
/// </summary>
public sealed record ConfirmReceiptOutcome(
    ConfirmReceiptStatus Status,
    ConfirmReceiptResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

public enum ConfirmReceiptStatus
{
    /// <summary>200 — this call performed the transition.</summary>
    Confirmed,

    /// <summary>
    /// 200 — the transaction was already <c>ITEM_DELIVERED</c>, so the current
    /// state is returned unchanged (07 §7.6b idempotency). Kept apart from
    /// <see cref="Confirmed"/> even though both map to 200: the two answer
    /// different questions in logs and tests ("did this call deliver?"), and
    /// collapsing them would hide a repeat that was expected to be a first call.
    /// </summary>
    AlreadyDelivered,

    /// <summary>404 <c>TRANSACTION_NOT_FOUND</c>.</summary>
    NotFound,

    /// <summary>403 <c>NOT_A_PARTY</c> — only the buyer may confirm receipt.</summary>
    NotAParty,

    /// <summary>
    /// 409 <c>INVALID_STATE_TRANSITION</c> — not in <c>PAYMENT_RECEIVED</c>, under
    /// emergency hold, or the state machine refused the trigger (07 §7.6b).
    /// </summary>
    InvalidStateTransition,
}
