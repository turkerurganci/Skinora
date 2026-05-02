namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Aggregates every per-user pre-condition the <c>POST /transactions</c>
/// endpoint enforces (T45 — 07 §7.3, 02 §8, §11, §14.3). Returned on its own
/// envelope by <c>GET /transactions/eligibility</c> and re-evaluated inside
/// <see cref="ITransactionCreationService.CreateAsync"/> so a stale form
/// cannot create a transaction the user is no longer entitled to.
/// </summary>
public interface ITransactionEligibilityService
{
    Task<EligibilityDto> GetAsync(Guid userId, CancellationToken cancellationToken);
}
