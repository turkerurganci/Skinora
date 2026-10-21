namespace Skinora.API.Services;

/// <summary>
/// Response body for P1 — <c>GET /platform/stats</c> (07 §10.1).
/// Landing-page trust signals (S01).
/// </summary>
/// <remarks>
/// T166 — <c>platformUptimePercent</c> was removed. It was a configuration
/// constant (<c>Platform:UptimePercent = 99.9</c>), never a measurement, and
/// the landing page presented it as one. A real figure needs the heartbeat
/// history (06 §3.23) and belongs with monitoring (F9 T170), not here.
/// </remarks>
public sealed record PlatformStatsResponse(
    int TotalCompletedTransactions);

/// <summary>
/// Response body for P2 — <c>GET /platform/maintenance</c> (07 §10.2).
/// Drives the C08 maintenance banner. <c>Type</c>, <c>Message</c> and
/// <c>PlannedEnd</c> are emitted as <c>null</c> when their underlying
/// SystemSetting carries the <c>"NONE"</c> sentinel.
/// </summary>
public sealed record PlatformMaintenanceResponse(
    bool Active,
    string? Type,
    string? Message,
    string? PlannedEnd);
