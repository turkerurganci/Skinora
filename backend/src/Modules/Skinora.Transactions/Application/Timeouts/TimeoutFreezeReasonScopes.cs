using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Timeouts;

/// <summary>
/// Maps <see cref="TimeoutFreezeReason"/> to the active transaction states it
/// applies to (T50 — 02 §3.3, 05 §4.4). Used by
/// <see cref="ITimeoutFreezeService.FreezeManyAsync"/> and
/// <see cref="ITimeoutFreezeService.ResumeManyAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>MAINTENANCE</b> covers every active state because a planned platform
/// outage halts the whole pipeline. <b>STEAM_OUTAGE</b> targets only the two
/// states whose deadlines depend on the platform being able to read Steam (the
/// seller's readiness re-check and the delivery verification window).
/// <b>BLOCKCHAIN_DEGRADATION</b> covers <c>SELLER_CONFIRMED</c>
/// because the only blockchain-bound timeout is <c>PaymentDeadline</c>.
/// </para>
/// <para>
/// <b>EMERGENCY_HOLD</b> is intentionally not supported by the scope helper:
/// admin emergency hold is single-transaction only (T59 + 05 §4.5) and goes
/// through <see cref="ITimeoutFreezeService.FreezeAsync"/> /
/// <see cref="ITimeoutFreezeService.ResumeAsync"/>. Calling
/// <see cref="For"/> with <c>EMERGENCY_HOLD</c> throws
/// <see cref="ArgumentException"/> so a misuse fails fast at the caller site.
/// </para>
/// </remarks>
public static class TimeoutFreezeReasonScopes
{
    private static readonly TransactionStatus[] AllActive =
    [
        TransactionStatus.CREATED,
        TransactionStatus.ACCEPTED,
        TransactionStatus.SELLER_CONFIRMED,
        TransactionStatus.PAYMENT_RECEIVED,
        TransactionStatus.ITEM_DELIVERED,
        TransactionStatus.FLAGGED,
    ];

    // Steam outage scope. In the P2P model the trade itself is unaffected by a
    // Steam outage — the two parties can still trade if Steam is up for them.
    // What breaks is the platform's ability to *verify* it, so the delivery
    // phase must freeze; otherwise the seller is wrongly recorded as having
    // failed to deliver (02 §23, 03 §11.2).
    private static readonly TransactionStatus[] SteamBound =
    [
        TransactionStatus.ACCEPTED,
        TransactionStatus.PAYMENT_RECEIVED,
    ];

    private static readonly TransactionStatus[] PaymentOnly =
    [
        TransactionStatus.SELLER_CONFIRMED,
    ];

    /// <summary>
    /// Returns the active states that participate in a bulk freeze/resume for
    /// the given platform-level reason. Throws for <c>EMERGENCY_HOLD</c> —
    /// that path is single-tx only and lives under
    /// <see cref="ITimeoutFreezeService.FreezeAsync"/>.
    /// </summary>
    public static IReadOnlyList<TransactionStatus> For(TimeoutFreezeReason reason) => reason switch
    {
        TimeoutFreezeReason.MAINTENANCE => AllActive,
        TimeoutFreezeReason.STEAM_OUTAGE => SteamBound,
        TimeoutFreezeReason.BLOCKCHAIN_DEGRADATION => PaymentOnly,
        TimeoutFreezeReason.EMERGENCY_HOLD =>
            throw new ArgumentException(
                "EMERGENCY_HOLD is single-transaction only — use FreezeAsync/ResumeAsync (T59).",
                nameof(reason)),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown freeze reason."),
    };
}
