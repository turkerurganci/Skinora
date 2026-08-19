namespace Skinora.API.Services;

/// <summary>
/// Composes the AD1 dashboard payload (07 §9.1) — summary counters and the
/// most recent fraud flags. Lives at the API composition root because it
/// spans Transactions and Fraud.
/// <para>
/// The platform Steam-bot snapshot this payload once carried went with the
/// bot custody layer in v3.0 (T132 — 02 §15): there are no platform Steam
/// accounts to report on.
/// </para>
/// </summary>
public interface IAdminDashboardService
{
    Task<AdminDashboardResponse> GetAsync(CancellationToken cancellationToken);
}
