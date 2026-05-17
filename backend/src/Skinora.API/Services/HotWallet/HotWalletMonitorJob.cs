using Microsoft.Extensions.Logging;

namespace Skinora.API.Services.HotWallet;

/// <summary>
/// Hangfire entry-point wrapper around <see cref="IHotWalletMonitorService"/>
/// (T77 — 05 §3.3). Mirrors the
/// <c>Skinora.API.Services.Reconciliation.ReconciliationJob</c> shape: the
/// schedule itself lives in <see cref="HotWalletMonitorJobRegistrar"/>;
/// this class only handles the DI / logging envelope and lets exceptions
/// propagate so Hangfire records the run as failed and applies retries.
/// </summary>
public sealed class HotWalletMonitorJob
{
    public const string RecurringJobId = "hot-wallet-monitor";

    /// <summary>
    /// Default cron — every 15 minutes. Admin override lives in
    /// <c>hot_wallet.monitor_cron</c> SystemSetting and is read once at
    /// startup by <see cref="HotWalletMonitorJobRegistrar"/>.
    /// </summary>
    public const string DefaultCron = "*/15 * * * *";

    private readonly IHotWalletMonitorService _service;
    private readonly ILogger<HotWalletMonitorJob> _logger;

    public HotWalletMonitorJob(
        IHotWalletMonitorService service,
        ILogger<HotWalletMonitorJob> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("HotWalletMonitorJob starting.");
        try
        {
            var outcome = await _service.RunAsync(cancellationToken);
            _logger.LogInformation(
                "HotWalletMonitorJob complete: checked={Checked} breaches={Breaches} block={Block}.",
                outcome.HotWalletChecked,
                outcome.BreachCount,
                outcome.BlockNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HotWalletMonitorJob failed.");
            throw;
        }
    }

    // Hangfire requires Expression<Action<T>> for recurring registration.
    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
