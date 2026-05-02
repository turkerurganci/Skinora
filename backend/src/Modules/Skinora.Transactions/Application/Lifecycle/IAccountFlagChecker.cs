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
}
