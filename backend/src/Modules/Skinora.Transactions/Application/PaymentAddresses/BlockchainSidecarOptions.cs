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

    /// <summary>Default for <see cref="TransferTimeoutSeconds"/>.</summary>
    public const int DefaultTransferTimeoutSeconds = 300;

    /// <summary>
    /// HTTP timeout, in seconds, for transfer BROADCAST calls (payout, sweep,
    /// refund family, cold wallet). A deposit-sourced broadcast waits on the
    /// sidecar for each resource step to land in a block (08 §3.3): the
    /// sidecar refuses to broadcast a transfer more than 150 s into the call,
    /// and after a broadcast waits up to ~75 s — past the node's 60 s
    /// transaction expiration — for the transfer's block before reclaiming
    /// delegated Energy. 300 s covers both with room for slow chain reads.
    /// A shorter budget abandons calls the sidecar is still running; the
    /// dispatcher's retry would then find the deposit's tokens already moved
    /// and the first transfer would never be recorded.
    /// </summary>
    public int TransferTimeoutSeconds { get; set; } = DefaultTransferTimeoutSeconds;

    /// <summary>Effective broadcast timeout; a non-positive setting falls back to the default.</summary>
    public TimeSpan ResolveTransferTimeout() =>
        TimeSpan.FromSeconds(TransferTimeoutSeconds > 0 ? TransferTimeoutSeconds : DefaultTransferTimeoutSeconds);

    /// <summary>
    /// Budget for a call that only reads the chain through the sidecar
    /// (transfer status, gas fee estimate): three times
    /// <see cref="TimeoutSeconds"/>, 30 s when that is unset. Kept apart from
    /// <see cref="ResolveTransferTimeout"/> so a stuck status read cannot hold
    /// the confirmation job for a broadcast's budget.
    /// </summary>
    public TimeSpan ResolveChainReadTimeout() =>
        TimeSpan.FromSeconds(TimeoutSeconds <= 0 ? 30 : TimeoutSeconds * 3);
}
