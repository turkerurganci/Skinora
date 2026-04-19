using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Infrastructure.Bootstrap;

namespace Skinora.API.Startup;

/// <summary>
/// IHostedService that runs <see cref="SettingsBootstrapService"/> at startup
/// so unconfigured SystemSetting rows are hydrated from env vars and missing
/// mandatory parameters abort the process (06 §8.9 fail-fast).
/// </summary>
/// <remarks>
/// Registered ahead of the outbox hook in <c>Program.cs</c> so the dispatcher
/// chain is only primed once configuration is proven complete. A throw from
/// <see cref="StartAsync"/> propagates and fails the host — the desired
/// behaviour for a misconfigured deploy.
/// </remarks>
public class SettingsBootstrapHook : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SettingsBootstrapHook> _logger;

    public SettingsBootstrapHook(
        IServiceScopeFactory scopeFactory,
        ILogger<SettingsBootstrapHook> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<SettingsBootstrapService>();

        try
        {
            await bootstrap.ExecuteAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogCritical(ex, "SystemSetting bootstrap failed — host will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
