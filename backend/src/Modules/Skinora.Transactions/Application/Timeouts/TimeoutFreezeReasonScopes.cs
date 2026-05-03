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
/// states whose deadlines wait on Steam-side action (the seller and buyer
/// trade-offer windows). <b>BLOCKCHAIN_DEGRADATION</b> covers <c>ITEM_ESCROWED</c>
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
        TransactionStatus.TRADE_OFFER_SENT_TO_SELLER,
        TransactionStatus.ITEM_ESCROWED,
        TransactionStatus.PAYMENT_RECEIVED,
        TransactionStatus.TRADE_OFFER_SENT_TO_BUYER,
        TransactionStatus.ITEM_DELIVERED,
        TransactionStatus.FLAGGED,
    ];

    private static readonly TransactionStatus[] SteamBound =
    [
        TransactionStatus.TRADE_OFFER_SENT_TO_SELLER,
        TransactionStatus.TRADE_OFFER_SENT_TO_BUYER,
    ];

    private static readonly TransactionStatus[] PaymentOnly =
    [
        TransactionStatus.ITEM_ESCROWED,
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
