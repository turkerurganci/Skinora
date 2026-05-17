namespace Skinora.Transactions.Application.PaymentAddresses;

/// <summary>
/// HTTP port over the blockchain sidecar's HD wallet derivation endpoint
/// (08 §3.2). Implementations translate transport failures into
/// <see cref="BlockchainSidecarStatus"/> so callers do not need to leak
/// <c>HttpClient</c>/<c>HttpResponseMessage</c> up the stack.
/// </summary>
public interface IBlockchainSidecarClient
{
    /// <summary>
    /// Derive a Tron deposit address for the given BIP-44 index. The
    /// allocator (.NET) owns index allocation and persistence; the sidecar
    /// only computes <c>m/44'/195'/0'/0/{index}</c> from the master mnemonic.
    /// </summary>
    Task<BlockchainSidecarDeriveResult> DeriveAddressAsync(
        int index,
        Guid transactionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Register a deposit address for post-cancel monitoring (T75 — 08 §3.4).
    /// Idempotent on <c>request.Address</c>: a duplicate call returns
    /// <c>BlockchainSidecarStatus.Success</c> without disturbing the
    /// sidecar's existing cursor/dedup state.
    /// </summary>
    Task<BlockchainSidecarStatus> StartPostCancelMonitoringAsync(
        PostCancelMonitorStartRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Drop a deposit address from post-cancel monitoring (manual admin
    /// stop, successful late refund, or transaction terminal cleanup —
    /// T75). Idempotent: returns Success whether or not the entry was
    /// being tracked.
    /// </summary>
    Task<BlockchainSidecarStatus> StopPostCancelMonitoringAsync(
        string address,
        CancellationToken cancellationToken);

    /// <summary>
    /// Snapshot the on-chain balances (TRX + every supported TRC-20 token)
    /// of the given Tron addresses (T76 — 05 §3.3). The sidecar captures
    /// the solid block height once per request and returns it alongside
    /// per-address token maps keyed by symbol (USDT / USDC / TRX).
    ///
    /// Returns <see cref="BlockchainSidecarBalancesResult.Unavailable"/>
    /// when the sidecar is unreachable, the response is malformed, or the
    /// upstream TronGrid call fails — callers (the reconciliation job)
    /// surface this as a missed snapshot rather than a false mismatch.
    /// </summary>
    Task<BlockchainSidecarBalancesResult> GetWalletBalancesAsync(
        IReadOnlyList<string> addresses,
        CancellationToken cancellationToken);
}

/// <summary>
/// Input payload for the sidecar <c>POST /api/monitor/post-cancel-start</c>
/// endpoint (T75). All timestamps are UTC.
/// </summary>
public sealed record PostCancelMonitorStartRequest(
    string Address,
    Guid PaymentAddressId,
    Guid TransactionId,
    string ExpectedContract,
    string ExpectedSymbol,
    DateTime CancelledAt,
    /// <summary>Recovery override — null when the cancel pipeline starts
    /// monitoring fresh. The startup recovery job passes the persisted
    /// <c>PaymentAddress.MonitoringStatus</c> here so the sidecar resumes
    /// at the same state instead of recomputing from <c>cancelledAt</c>.
    /// </summary>
    string? InitialState = null,
    DateTime? InitialStateExpiresAt = null);

/// <summary>
/// Discriminated outcome — distinguishes a successful derivation (200) from
/// an unconfigured sidecar (503) or any other transport/upstream failure.
/// </summary>
public sealed record BlockchainSidecarDeriveResult(
    BlockchainSidecarStatus Status,
    string? Address,
    string? DerivationPath);

public enum BlockchainSidecarStatus
{
    /// <summary>Sidecar returned a success envelope (HTTP 200).</summary>
    Success,

    /// <summary>Sidecar reported <c>HD_WALLET_NOT_CONFIGURED</c> (HTTP 503).</summary>
    NotConfigured,

    /// <summary>Sidecar 4xx (validation) — caller surfaces to operator/log.</summary>
    InvalidRequest,

    /// <summary>Sidecar 5xx, transport failure, or timeout — caller retries.</summary>
    Unavailable,
}

/// <summary>
/// Per-address balance snapshot returned by <see cref="IBlockchainSidecarClient.GetWalletBalancesAsync"/>
/// (T76 — 05 §3.3). <c>Tokens</c> is keyed by symbol (USDT / USDC / TRX) and
/// holds the raw integer amount as reported by TronGrid (6 decimals for the
/// stablecoins, SUN for TRX). The caller converts to a comparable scale
/// (decimal at scale 6, see 09 §14.3) before subtracting against the ledger.
/// </summary>
public sealed record BlockchainSidecarAddressBalances(
    string Address,
    IReadOnlyDictionary<string, string> Tokens);

/// <summary>
/// Discriminated outcome of <see cref="IBlockchainSidecarClient.GetWalletBalancesAsync"/>.
/// </summary>
public sealed record BlockchainSidecarBalancesResult(
    BlockchainSidecarStatus Status,
    long? BlockNumber,
    IReadOnlyList<BlockchainSidecarAddressBalances>? Balances)
{
    public static readonly BlockchainSidecarBalancesResult Unavailable =
        new(BlockchainSidecarStatus.Unavailable, null, null);
}
