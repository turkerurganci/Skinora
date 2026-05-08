namespace Skinora.API.Services;

/// <summary>
/// Bound from the <c>Platform</c> section of <c>appsettings.json</c>.
/// Hosts the values exposed by P1 / P2 that aren't per-tenant SystemSettings.
/// </summary>
/// <remarks>
/// 07 §10.1 lists <c>platformUptimePercent</c> in the response payload but
/// does not document a source of truth. T63a ships a config-driven constant
/// so the contract is met today; the heartbeat-derived computation is on
/// the long-term backlog (no task assigned yet — see T63a report Notes).
/// </remarks>
public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    /// <summary>
    /// Value emitted as <c>platformUptimePercent</c>. The 07 §10.1 example
    /// shows <c>99.9</c>; appsettings binding overrides this default.
    /// </summary>
    public decimal UptimePercent { get; set; } = 99.9m;
}
