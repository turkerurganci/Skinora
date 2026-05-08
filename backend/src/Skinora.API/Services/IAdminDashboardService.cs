namespace Skinora.API.Services;

/// <summary>
/// Composes the AD1 dashboard payload (07 §9.1) — summary counters,
/// platform Steam-bot snapshot, and the most recent fraud flags. Lives at
/// the API composition root because it spans Transactions, Fraud and Steam.
/// </summary>
public interface IAdminDashboardService
{
    Task<AdminDashboardResponse> GetAsync(CancellationToken cancellationToken);
}
