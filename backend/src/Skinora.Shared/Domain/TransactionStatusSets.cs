using Skinora.Shared.Enums;

namespace Skinora.Shared.Domain;

/// <summary>
/// Single source of truth for the status sets that query surfaces use to decide
/// whether a transaction is still "active" (05 §4.1, 07 §9.1 / §9.6 / §9.16 /
/// §9.22a).
///
/// <para>
/// <b>Why this type exists.</b> Before it, every surface spelled the terminal
/// set out by hand — three <c>_terminalStates</c> arrays plus five inline
/// <c>t.Status != …</c> chains. When <see cref="TransactionStatus.REFUNDED"/>
/// became terminal (WP5 buyer-favor dispute resolution, extended by T129's
/// settlement reversal), the change reached some copies and missed others, and
/// each copy carried an XML comment asserting parity it did not have. The drift
/// was found and half-fixed three separate times (T117 validation → T118 fixed
/// <c>AdminUserActivityProvider</c>; T133a measured
/// <c>ActiveTransactionCounter</c> / <c>UserActiveTransactionChecker</c> and
/// recorded them; the fraud module's three predicates were never spotted at
/// all). Copies cannot be kept in sync by comment — they have to stop being
/// copies.
/// </para>
///
/// <para>
/// <b>The guard.</b> <c>TransactionStatusSetsTests</c> derives the expected
/// terminal set from <c>TransactionStateMachine</c> itself — a status is
/// terminal exactly when it permits no outgoing trigger — and asserts it equals
/// <see cref="Terminal"/>. Adding a status to the state machine without
/// updating this set (or vice versa) fails that test, so the two can no longer
/// drift apart silently.
/// </para>
/// </summary>
public static class TransactionStatusSets
{
    /// <summary>
    /// Statuses a transaction can never leave (05 §4.1). "Active" is defined
    /// everywhere as <i>not</i> in this set, which deliberately keeps
    /// <see cref="TransactionStatus.FLAGGED"/> active — a flagged transaction
    /// is still awaiting an admin decision (07 §9.21).
    ///
    /// <para>
    /// <see cref="TransactionStatus.REFUNDED"/> belongs here: the money went
    /// back to the buyer and no trigger leads out of it. It is kept as its own
    /// status rather than folded into <c>CANCELLED_ADMIN</c> so refunds stay
    /// first-class in reporting (06 §2.11).
    /// </para>
    /// </summary>
    public static readonly TransactionStatus[] Terminal =
    [
        TransactionStatus.COMPLETED,
        TransactionStatus.CANCELLED_TIMEOUT,
        TransactionStatus.CANCELLED_SELLER,
        TransactionStatus.CANCELLED_BUYER,
        TransactionStatus.CANCELLED_ADMIN,
        TransactionStatus.REFUNDED,
    ];

    /// <summary>
    /// Terminal cancelled/unwound states behind the admin "İptal" status group
    /// (04 §8.4, 07 §9.6). <see cref="TransactionStatus.COMPLETED"/> is the only
    /// terminal status that is not a cancellation, so this is
    /// <see cref="Terminal"/> minus that one.
    ///
    /// <para>
    /// Distinct from the S20 user-activity "İptal" stat (04 §8.9.2), which
    /// counts only the four <c>CANCELLED_*</c> values on purpose: there,
    /// <c>REFUNDED</c> is reported separately rather than merged into the
    /// cancellation tally.
    /// </para>
    /// </summary>
    public static readonly TransactionStatus[] Cancelled =
    [
        TransactionStatus.CANCELLED_TIMEOUT,
        TransactionStatus.CANCELLED_SELLER,
        TransactionStatus.CANCELLED_BUYER,
        TransactionStatus.CANCELLED_ADMIN,
        TransactionStatus.REFUNDED,
    ];
}
