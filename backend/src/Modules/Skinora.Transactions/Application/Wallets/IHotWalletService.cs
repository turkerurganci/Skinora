using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Wallets;

/// <summary>
/// Admin-facing orchestrator for hot wallet operational moves (T77 — 05 §3.3).
/// The MVP exposes one verb — initiate a manual hot → cold consolidation
/// transfer; the implementation calls the blockchain sidecar (signing-only)
/// and persists a <see cref="Skinora.Payments.Domain.Entities.ColdWalletTransfer"/>
/// ledger row plus a <c>COLD_WALLET_TRANSFER_INITIATED</c> AuditLog entry
/// inside the same SaveChanges scope so reconciliation (T76) does not see a
/// transient mismatch.
/// </summary>
public interface IHotWalletService
{
    /// <summary>
    /// Initiate a hot → cold wallet TRC-20 transfer. <paramref name="amount"/>
    /// is a positive decimal (scale 6 truncated, 09 §14.3); the destination
    /// cold wallet address is read from the
    /// <c>reconciliation.cold_wallet_address</c> SystemSetting. Returns a
    /// discriminated outcome describing where the request stopped — admin
    /// controller maps the result to the HTTP envelope.
    /// </summary>
    Task<HotWalletColdTransferOutcome> InitiateColdTransferAsync(
        decimal amount,
        StablecoinType token,
        Guid initiatingAdminId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of <see cref="IHotWalletService.InitiateColdTransferAsync"/>.
/// </summary>
public abstract record HotWalletColdTransferOutcome
{
    /// <summary>Sidecar broadcast succeeded; ledger and audit rows written.</summary>
    public sealed record Success(
        long ColdTransferId,
        string TxHash,
        decimal Amount,
        StablecoinType Token,
        string FromAddress,
        string ToAddress) : HotWalletColdTransferOutcome;

    /// <summary>Caller-supplied amount is zero, negative, or out of scale.</summary>
    public sealed record InvalidAmount(string Reason) : HotWalletColdTransferOutcome;

    /// <summary>
    /// Hot wallet address SystemSetting is unconfigured or set to the
    /// 'NONE' sentinel — admin must populate it before initiating a transfer
    /// (05 §3.3). Same gate the reconciliation hot wallet scope uses.
    /// </summary>
    public sealed record HotWalletNotConfigured : HotWalletColdTransferOutcome;

    /// <summary>
    /// Cold wallet address SystemSetting is unconfigured or 'NONE' — admin
    /// must populate it before initiating a transfer.
    /// </summary>
    public sealed record ColdWalletNotConfigured : HotWalletColdTransferOutcome;

    /// <summary>
    /// Sidecar / network failure — caller should surface as a retryable
    /// 502 and the operator retries. The ledger row is NOT written in this
    /// path; no audit entry either, so an explicit retry is idempotent.
    /// </summary>
    public sealed record SidecarUnavailable(string Status) : HotWalletColdTransferOutcome;
}
