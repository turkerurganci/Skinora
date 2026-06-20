namespace Skinora.API.Monitoring;

/// <summary>
/// WP16 platform health probe job (05 §4.4, 02 §3.3). Hangfire recurring job
/// target — sweeps the sidecar <c>/health</c> endpoints and raises an admin
/// alert on each outage / recovery transition.
/// </summary>
public interface IPlatformHealthProbeJob
{
    Task ProbeAsync();
}
