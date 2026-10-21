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

/// <summary>
/// Response body for P3 — <c>GET /platform/payout-sources</c> (07 §10.3, T167).
/// One entry per network whose hot wallet is configured, enum order. A network
/// whose address is unset is left out rather than sent as <c>null</c>.
/// </summary>
/// <remarks>
/// Why it is public (T167, MASAK): a Turkish exchange asks its customer to
/// declare the sender of a deposit that comes from a wallet not registered
/// with any crypto-asset service provider (Tedbirler Yönetmeliği m. 24/A-6).
/// Every Skinora payout on a network leaves from this one address, so a seller
/// can declare it once — and needs to know it before the first payout lands.
/// The address is already public on chain to everyone who has received a payout.
/// </remarks>
public sealed record PlatformPayoutSourcesResponse(
    IReadOnlyList<PlatformPayoutSourceDto> Sources);

/// <summary>One network's payout sender; <c>Address</c> in display form (EIP-55 on Ethereum, 06 §1.4).</summary>
public sealed record PlatformPayoutSourceDto(
    Skinora.Shared.Enums.BlockchainNetwork Network,
    string Address);
