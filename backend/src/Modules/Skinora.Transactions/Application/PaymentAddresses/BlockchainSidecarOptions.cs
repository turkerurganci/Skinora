namespace Skinora.Transactions.Application.PaymentAddresses;

/// <summary>
/// Binding target for the <c>BlockchainSidecar</c> configuration section. Holds
/// the sidecar HTTP base URL and the shared internal-key used by the
/// <c>X-Internal-Key</c> header (05 §3.4 — service-to-service auth).
/// Mirrors <see cref="Skinora.Steam.Application.Inventory.SteamSidecarOptions"/>
/// so deployment configuration stays symmetric.
/// </summary>
public sealed class BlockchainSidecarOptions
{
    public const string SectionName = "BlockchainSidecar";

    /// <summary>
    /// Base URL of the blockchain sidecar
    /// (e.g. <c>http://skinora-blockchain-sidecar:5200</c>). Trailing slashes
    /// are tolerated by <see cref="HttpClient"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Pre-shared key sent in the <c>X-Internal-Key</c> header on every
    /// outbound call. Mirrors <c>INTERNAL_KEY</c> on the sidecar (see
    /// <c>sidecar-blockchain/src/api/middleware.ts</c>).
    /// </summary>
    public string InternalKey { get; set; } = string.Empty;

    /// <summary>
    /// Per-request HTTP timeout in seconds. HD derivation is in-process
    /// crypto on the sidecar — under 50ms in practice — so the default is
    /// tight enough to surface a stuck sidecar quickly without flaking under
    /// container cold starts.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;
}
