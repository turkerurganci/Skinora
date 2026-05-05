namespace Skinora.Users.Application.MultiAccount;

/// <summary>
/// Outcome of a single <see cref="IMultiAccountDetector.EvaluateAsync"/>
/// invocation. Discriminated by <see cref="Status"/> so callers (logging,
/// metrics) can distinguish "no signal" from "already flagged" from "freshly
/// flagged" without inspecting the DB.
/// </summary>
public sealed record MultiAccountEvaluationResult(
    MultiAccountEvaluationStatus Status,
    MultiAccountMatchType? PrimaryMatchType,
    string? PrimaryMatchValue,
    int LinkedAccountCount,
    int SupportingSignalCount,
    Guid? FlagId)
{
    public static MultiAccountEvaluationResult NoSignal() =>
        new(MultiAccountEvaluationStatus.NoSignal, null, null, 0, 0, null);

    public static MultiAccountEvaluationResult AlreadyFlagged() =>
        new(MultiAccountEvaluationStatus.AlreadyFlagged, null, null, 0, 0, null);

    public static MultiAccountEvaluationResult Flagged(
        MultiAccountMatchType matchType,
        string matchValue,
        int linkedAccountCount,
        int supportingSignalCount,
        Guid flagId) =>
        new(MultiAccountEvaluationStatus.Flagged,
            matchType, matchValue, linkedAccountCount, supportingSignalCount, flagId);
}

public enum MultiAccountEvaluationStatus
{
    /// <summary>No strong signal fired — user is clean (supporting-only matches do not flag).</summary>
    NoSignal,

    /// <summary>User already carries a non-rejected MULTI_ACCOUNT account-level flag — no new row staged.</summary>
    AlreadyFlagged,

    /// <summary>Strong signal fired and a new flag row was staged + persisted.</summary>
    Flagged,
}

/// <summary>
/// Strong-signal match types — the value that drives the
/// <c>flagDetail.matchType</c> field (07 §9.3). Only wallet-address matches
/// are strong; IP / device fingerprint / source address are supporting and
/// surface in <c>supportingSignals</c>.
/// </summary>
public enum MultiAccountMatchType
{
    WALLET_PAYOUT,
    WALLET_REFUND,
}
