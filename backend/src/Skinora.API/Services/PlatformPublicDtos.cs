namespace Skinora.API.Services;

/// <summary>
/// Response body for P1 — <c>GET /platform/stats</c> (07 §10.1).
/// Landing-page trust signals (S01).
/// </summary>
public sealed record PlatformStatsResponse(
    int TotalCompletedTransactions,
    decimal PlatformUptimePercent);

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
