namespace Skinora.Platform.Application.Settings;

/// <summary>
/// Post-write side-effect hook for a <c>SystemSetting</c> change whose target
/// lives outside the Platform module — specifically the Hangfire recurring
/// jobs registered in the API host (WP14). The Platform module owns the seam;
/// the API host supplies a real implementation that re-registers a
/// cron-scheduled job so an admin cadence change takes effect without a host
/// restart.
/// </summary>
/// <remarks>
/// Invoked by <see cref="SystemSettingsService"/> <b>after</b> the row and its
/// audit entry are committed. Implementations are best-effort: the
/// authoritative DB write has already succeeded, so a propagation failure is
/// logged rather than surfaced to the admin. The default
/// <see cref="NoOpSettingChangePropagator"/> keeps unit tests and non-API
/// hosts resolving cleanly.
/// </remarks>
public interface ISettingChangePropagator
{
    Task PropagateAsync(string key, string value, CancellationToken cancellationToken);
}

/// <summary>
/// No-op default — used by the startup bootstrap, unit tests, and any host that
/// does not register cron-scheduled jobs. The API host replaces this with a
/// real propagator in its composition root.
/// </summary>
public sealed class NoOpSettingChangePropagator : ISettingChangePropagator
{
    public static NoOpSettingChangePropagator Instance { get; } = new();

    public Task PropagateAsync(string key, string value, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
