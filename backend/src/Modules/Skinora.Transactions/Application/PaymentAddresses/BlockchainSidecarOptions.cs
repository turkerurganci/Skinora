using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.PaymentAddresses;

/// <summary>
/// Binding target for one network's blockchain service: its HTTP base URL and
/// its own internal key for the <c>X-Internal-Key</c> header (05 §3.4 —
/// service-to-service auth). Mirrors
/// <see cref="Skinora.Steam.Application.Inventory.SteamSidecarOptions"/> so
/// deployment configuration stays symmetric.
/// </summary>
/// <remarks>
/// <para>
/// <b>One instance per network (T148 — 05 §3.3a).</b> TRON binds the
/// <c>BlockchainSidecar</c> section as the default (unnamed) options instance,
/// exactly as before the network dimension, so existing deployments need no
/// change. Ethereum binds <c>EthereumSidecar</c> as the named instance
/// <c>"ETHEREUM"</c>. An empty <see cref="BaseUrl"/> means the network has no
/// service: the backend sends nothing there and every caller of that network
/// fails closed (<c>IBlockchainServiceRegistry</c>) — the other network keeps
/// working.
/// </para>
/// </remarks>
public sealed class BlockchainSidecarOptions
{
    /// <summary>TRON's section (pre-F8 name, kept).</summary>
    public const string SectionName = "BlockchainSidecar";

    /// <summary>Ethereum's section (T148). Absent until the Ethereum service exists (T150).</summary>
    public const string EthereumSectionName = "EthereumSidecar";

    /// <summary>Configuration section of <paramref name="network"/>'s service.</summary>
    public static string SectionNameFor(BlockchainNetwork network) => network switch
    {
        BlockchainNetwork.TRON => SectionName,
        BlockchainNetwork.ETHEREUM => EthereumSectionName,
        _ => throw new ArgumentOutOfRangeException(nameof(network), network, "No service section for this network."),
    };

    /// <summary>
    /// Options-instance name of <paramref name="network"/>: TRON is the default
    /// instance (so every pre-F8 <c>IOptions&lt;BlockchainSidecarOptions&gt;</c>
    /// reader still sees TRON), other networks are named after the enum value.
    /// </summary>
    public static string OptionsNameFor(BlockchainNetwork network) =>
        network == BlockchainNetwork.TRON ? Options.DefaultName : network.ToString();

    /// <summary>True when the network has a service address to talk to.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

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
    /// HTTP timeout, in seconds, for the transfer BROADCAST calls that go
    /// through <c>HttpBlockchainTransferClient</c> (payout, sweep, refund
    /// family). The admin cold-wallet transfer is NOT one of them — it goes
    /// through <c>HttpBlockchainSidecarClient</c>, waits for no block and
    /// keeps <see cref="TimeoutSeconds"/>. A deposit-sourced broadcast waits on the
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
    /// (transfer status, gas fee estimate, balance snapshot — T158
    /// doğrulaması (i)): three times
    /// <see cref="TimeoutSeconds"/>, 30 s when that is unset. Kept apart from
    /// <see cref="ResolveTransferTimeout"/> so a stuck status read cannot hold
    /// the confirmation job for a broadcast's budget.
    /// </summary>
    public TimeSpan ResolveChainReadTimeout() =>
        TimeSpan.FromSeconds(TimeoutSeconds <= 0 ? 30 : TimeoutSeconds * 3);
}
