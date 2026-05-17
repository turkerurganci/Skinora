using Microsoft.Extensions.Logging;
using Skinora.Transactions.Application.Reconciliation;

namespace Skinora.API.Services.Reconciliation;

/// <summary>
/// Hangfire entry-point wrapper around <see cref="IReconciliationService"/>
/// (T76 — 05 §3.3). The schedule itself lives in
/// <see cref="ReconciliationJobRegistrar"/>; this type only handles the
/// scheduler ↔ DI hand-off and the per-run logging envelope. Errors are
/// logged but allowed to surface so Hangfire records the run as failed and
/// triggers its default retry policy.
/// </summary>
public sealed class ReconciliationJob
{
    public const string RecurringJobId = "blockchain-reconciliation";

    /// <summary>
    /// Default cron — daily at 03:00 UTC. The admin-tunable override lives in
    /// the <c>reconciliation.schedule_cron</c> SystemSetting, read once at
    /// startup by <see cref="ReconciliationJobRegistrar"/>. 03:00 UTC sits in
    /// the documented quiet window between the retention sweeps and the
    /// morning trading peak.
    /// </summary>
    public const string DefaultCron = "0 3 * * *";

    private readonly IReconciliationService _service;
    private readonly ILogger<ReconciliationJob> _logger;

    public ReconciliationJob(
        IReconciliationService service,
        ILogger<ReconciliationJob> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("ReconciliationJob starting.");
        try
        {
            var outcome = await _service.RunAsync(cancellationToken);
            _logger.LogInformation(
                "ReconciliationJob complete: deposits={Deposits} hot={Hot} cold={Cold} mismatches={Mismatches} block={Block}.",
                outcome.DepositAddressesChecked,
                outcome.HotWalletChecked,
                outcome.ColdWalletChecked,
                outcome.MismatchCount,
                outcome.BlockNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReconciliationJob failed.");
            throw;
        }
    }

    // Hangfire requires Expression<Action<T>> for the recurring registration.
    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
