namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Read port over <c>FraudFlag</c> rows scoped to a single user (02 §14.0
/// "Hesap flag'i"). The implementation lives in <c>Skinora.Fraud</c> because
/// that module owns the entity; <c>Skinora.Transactions</c> stays free of
/// a Fraud project reference (avoids the would-be cycle Fraud → Transactions
/// → Fraud).
/// </summary>
public interface IAccountFlagChecker
{
    /// <summary>
    /// Returns <c>true</c> when the user has at least one non-rejected,
    /// non-soft-deleted <c>ACCOUNT_LEVEL</c> flag — i.e. a flag with
    /// <c>Status ∈ {PENDING, APPROVED}</c>. <c>REJECTED</c> means an admin
    /// dismissed the flag and the user is unblocked (06 §3.12).
    /// </summary>
    Task<bool> HasActiveAccountFlagAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns <c>true</c> when the user has a <c>PENDING</c>, non-soft-deleted
    /// <c>ACCOUNT_LEVEL</c> flag of <paramref name="type"/> — including one
    /// staged on the change tracker but not yet saved. The unsaved half is the
    /// point: two qualifying events for one seller can be evaluated inside a
    /// single unit of work (the deadline scanner flushes a whole batch before
    /// evaluating), and the second evaluation must see the first one's flag
    /// (02 §14.2 non-delivery sanction).
    /// </summary>
    Task<bool> HasPendingAccountFlagAsync(
        Guid userId,
        Skinora.Shared.Enums.FraudFlagType type,
        CancellationToken cancellationToken);
}
