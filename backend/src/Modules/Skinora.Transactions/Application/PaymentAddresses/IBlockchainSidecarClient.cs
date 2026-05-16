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
}

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
