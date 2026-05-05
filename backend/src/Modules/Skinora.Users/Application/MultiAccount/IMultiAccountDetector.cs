namespace Skinora.Users.Application.MultiAccount;

/// <summary>
/// Multi-account detection port (T56 — 02 §14.3, 03 §7.4). Evaluates the
/// user against every other active account for the documented signal set
/// and, when a <em>strong</em> signal (wallet address match) is found,
/// stages an <c>ACCOUNT_LEVEL</c> <c>MULTI_ACCOUNT</c> fraud flag. Supporting
/// signals (IP, device fingerprint, payment source address) are surfaced as
/// <c>supportingSignals</c> evidence in the flag detail but never flag on
/// their own (02 §14.3 "tek başına flag sebebi değildir").
/// </summary>
/// <remarks>
/// <para>
/// The port lives in <c>Skinora.Users</c> so both <c>Skinora.Users</c>
/// (wallet address change — T34) and any future caller can depend on it
/// without referencing <c>Skinora.Fraud</c>. The implementation lives in
/// <c>Skinora.Fraud</c> because that module already references both
/// <c>Skinora.Users</c> and <c>Skinora.Transactions</c>; reversing the
/// direction would create a project cycle (mirrors the
/// <see cref="Skinora.Transactions.Application.Lifecycle.IAccountFlagChecker"/>
/// pattern from T54).
/// </para>
/// <para>
/// <b>Idempotency:</b> if the user already has a non-rejected
/// <c>ACCOUNT_LEVEL</c> <c>MULTI_ACCOUNT</c> flag, <see cref="EvaluateAsync"/>
/// returns <see cref="MultiAccountEvaluationResult.AlreadyFlagged"/> without
/// staging a new row — admin is reviewing the existing one.
/// </para>
/// <para>
/// <b>Atomicity:</b> the implementation owns its own <c>SaveChanges</c>.
/// Callers should invoke it <em>after</em> their own commit so a multi-account
/// signal does not leak from an aborted parent transaction.
/// </para>
/// </remarks>
public interface IMultiAccountDetector
{
    /// <summary>
    /// Evaluate the multi-account signal set for the supplied user. When a
    /// strong signal fires, stage and persist a <c>MULTI_ACCOUNT</c>
    /// account-level fraud flag and return its identifier.
    /// </summary>
    /// <param name="userId">User to evaluate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MultiAccountEvaluationResult> EvaluateAsync(
        Guid userId, CancellationToken cancellationToken);
}
