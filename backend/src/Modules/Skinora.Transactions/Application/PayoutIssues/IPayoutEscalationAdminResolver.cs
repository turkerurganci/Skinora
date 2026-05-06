namespace Skinora.Transactions.Application.PayoutIssues;

/// <summary>
/// Resolves which admin user a SellerPayoutIssue should be escalated to.
/// 06 §3.8a's <c>CK_SellerPayoutIssues_Status_Invariants</c> requires
/// <c>EscalatedToAdminId NOT NULL</c> when the row reaches ESCALATED, so the
/// payout-issue service must obtain a valid admin guid synchronously before
/// promoting state.
/// </summary>
/// <remarks>
/// <para>
/// The implementation lives outside <c>Skinora.Transactions</c> because the
/// admin role assignment table belongs to <c>Skinora.Admin</c>, which this
/// module cannot reference (would be a project cycle — Disputes/Fraud follow
/// the same convention). The production resolver is wired at the
/// <c>Skinora.API</c> composition root and queries
/// <c>AdminUserRole</c> for an active admin.
/// </para>
/// <para>
/// Returning <c>null</c> means no admin exists — the service treats that as
/// an unrecoverable configuration error and surfaces it via
/// <c>InvalidOperationException</c>. Production deployments are expected to
/// always have at least one admin assignment (06 §8.9 SYSTEM seed plus the
/// initial bootstrap admin). Tests inject their own resolver returning a
/// known guid (typically a freshly seeded admin user).
/// </para>
/// </remarks>
public interface IPayoutEscalationAdminResolver
{
    Task<Guid?> ResolveAdminUserIdAsync(CancellationToken cancellationToken);
}
